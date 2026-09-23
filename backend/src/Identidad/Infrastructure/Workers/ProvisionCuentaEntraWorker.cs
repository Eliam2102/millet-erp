using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Domain.Exceptions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Infrastructure.Workers;

/// <summary>
/// Crea en Entra la cuenta de los colaboradores dados de alta con "Cuenta
/// Microsoft nueva" (plan 15, F4, camino B) y les envía el acceso.
///
/// <para>
/// <b>La cola es el propio usuario.</b> El alta deja al Usuario en
/// <see cref="EstadoAcceso.ProvisionandoCuenta"/> dentro de la misma
/// transacción que el Empleado (ADR-0052), así que esa fila cumple el papel
/// del evento de outbox: solo existe si el alta hizo commit, y la llamada al
/// directorio ocurre siempre después. Identidad no tiene outbox propio y un
/// viaje por Service Bus hacia el mismo módulo no agrega garantías.
/// </para>
///
/// <para>Por cada usuario, en su propia transacción y con la fila bloqueada
/// (<c>FOR UPDATE SKIP LOCKED</c>, para varias instancias):</para>
/// <list type="number">
///   <item>Crea la cuenta (<see cref="IEntraDirectorioPort.CrearCuentaAsync"/>),
///         idempotente por el id del empleado: un reintento adopta la cuenta
///         ya creada y le da una contraseña temporal nueva.</item>
///   <item>Envía el correo de acceso al <c>EmailContacto</c> del empleado.</item>
///   <item>Vincula el OID real y registra el envío: el usuario pasa a
///         <see cref="EstadoAcceso.PendientePrimerAcceso"/>.</item>
/// </list>
/// Si algo falla, el usuario queda en <see cref="EstadoAcceso.ErrorProvision"/>
/// con el motivo y el admin puede reintentar. Si el correo falla después de
/// crear la cuenta, el OID no se vincula: el reintento adopta la cuenta y
/// reenvía con otra contraseña. La contraseña solo vive en memoria durante
/// el ciclo (D6).
/// </summary>
public sealed class ProvisionCuentaEntraWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;
    private readonly ILogger<ProvisionCuentaEntraWorker> _logger;

    public ProvisionCuentaEntraWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EntraDirectorioOptions> options,
        ILogger<ProvisionCuentaEntraWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.CurrentValue.Provision.Disabled)
        {
            _logger.LogInformation("[ProvisionCuentaEntraWorker] Disabled — loop no inicia.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcesarPendientesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProvisionCuentaEntraWorker] Error inesperado.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.CurrentValue.Provision.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    /// <summary>
    /// Un ciclo: atiende hasta <c>Entra:Provision:BatchSize</c> usuarios en
    /// provisión. Devuelve cuántos procesó (con éxito o con error). Público
    /// para que las pruebas corran un ciclo sin esperar el intervalo.
    /// </summary>
    public async Task<int> ProcesarPendientesAsync(CancellationToken ct)
    {
        List<Guid> pendientes;
        using (var scope = _scopeFactory.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            pendientes = await identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.EstadoAcceso == EstadoAcceso.ProvisionandoCuenta)
                .OrderBy(u => u.Id)
                .Select(u => u.Id)
                .Take(_options.CurrentValue.Provision.BatchSize)
                .ToListAsync(ct);
        }

        var procesados = 0;
        foreach (var usuarioId in pendientes)
        {
            if (await ProvisionarAsync(usuarioId, ct)) procesados++;
        }
        return procesados;
    }

    private async Task<bool> ProvisionarAsync(Guid usuarioId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var identidad = sp.GetRequiredService<IdentidadDbContext>();
        var compartido = sp.GetRequiredService<CompartidoDbContext>();
        var directorio = sp.GetRequiredService<IEntraDirectorioPort>();
        var correoSaliente = sp.GetRequiredService<ICorreoSalientePort>();
        var clock = sp.GetRequiredService<IClock>();

        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ProvisionCuentaEntraWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        using var correlacion = sp.GetRequiredService<IAuditCorrelationContext>().Begin(Guid.CreateVersion7());

        await using var tx = await identidad.Database.BeginTransactionAsync(ct);

        // Tabla y columnas según la convención snake_case del repo.
        var usuario = await identidad.Usuarios
            .FromSqlRaw(
                """
                SELECT * FROM identidad.usuarios
                WHERE id = {0} AND estado_acceso = {1}
                FOR UPDATE SKIP LOCKED
                """,
                usuarioId, (short)EstadoAcceso.ProvisionandoCuenta)
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);
        if (usuario is null)
        {
            // Otra instancia lo tiene bloqueado o ya cambió de estado.
            await tx.CommitAsync(ct);
            return false;
        }

        try
        {
            var empleado = await compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
                .Where(e => e.UsuarioId == usuario.Id)
                .Select(e => new { e.Id, e.Nombre, e.EmailContacto })
                .FirstOrDefaultAsync(ct)
                ?? throw new BusinessRuleException(
                    "PROVISION_SIN_EMPLEADO", "El usuario no tiene un empleado vinculado.");
            if (string.IsNullOrWhiteSpace(empleado.EmailContacto))
            {
                throw new BusinessRuleException(
                    "PROVISION_SIN_CORREO_CONTACTO",
                    "El empleado no tiene correo de contacto para enviarle el acceso.");
            }

            var creada = await directorio.CrearCuentaAsync(
                new SolicitudCuentaEntra(
                    usuario.Email, usuario.Nombre, empleado.EmailContacto, empleado.Id.ToString()),
                ct);

            await correoSaliente.EnviarAccesoColaboradorAsync(
                new CorreoAccesoColaborador(
                    empleado.EmailContacto,
                    empleado.Nombre,
                    creada.Cuenta.Upn,
                    creada.ContrasenaTemporal,
                    _options.CurrentValue.UrlInicioSesion),
                ct);

            usuario.VincularEntraOid(creada.Cuenta.ObjectId);
            usuario.RegistrarEnvioAcceso(clock.UtcNow);

            _logger.LogInformation(
                "[ProvisionCuentaEntraWorker] Cuenta {Upn} creada y acceso enviado (usuario {UsuarioId}).",
                creada.Cuenta.Upn, usuario.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var motivo = ex is DomainException de ? $"{de.Code}: {de.Message}" : ex.Message;
            usuario.MarcarErrorProvision(motivo);
            _logger.LogWarning(ex,
                "[ProvisionCuentaEntraWorker] No se pudo provisionar la cuenta del usuario {UsuarioId}.",
                usuario.Id);
        }

        await identidad.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
