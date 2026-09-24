using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>
/// Estado del acceso al ERP de un colaborador (pestaña "Acceso" del
/// detalle del empleado, F7). 404 <c>COLABORADOR_SIN_ACCESO</c> si el
/// empleado no tiene usuario.
/// </summary>
public sealed record ObtenerAccesoColaboradorQuery(Guid EmpleadoId) : IRequest<EstadoAccesoColaboradorResponse>;

/// <summary>
/// El admin vuelve a pedir la cuenta después de un
/// <see cref="EstadoAcceso.ErrorProvision"/> (F4). El worker la retoma en
/// su siguiente ciclo.
/// </summary>
public sealed record ReintentarProvisionColaboradorCommand(Guid EmpleadoId) : IRequest<EstadoAccesoColaboradorResponse>;

/// <summary>
/// "Reenviar acceso" (F4): contraseña temporal nueva en Entra y correo al
/// <c>EmailContacto</c> del empleado. Solo para una cuenta ya creada que
/// todavía no inicia sesión.
/// </summary>
public sealed record ReenviarAccesoColaboradorCommand(Guid EmpleadoId) : IRequest<EstadoAccesoColaboradorResponse>;

public sealed record EstadoAccesoColaboradorResponse(
    Guid EmpleadoId,
    Guid UsuarioId,
    string Email,
    EstadoAcceso EstadoAcceso,
    string? MotivoErrorProvision,
    DateTimeOffset? AccesoEnviadoEn,
    DateTimeOffset? PrimerAccesoEn,
    string? EmailContacto,
    bool UsuarioActivo);

public sealed class AccesoColaboradorHandlers :
    IRequestHandler<ObtenerAccesoColaboradorQuery, EstadoAccesoColaboradorResponse>,
    IRequestHandler<ReintentarProvisionColaboradorCommand, EstadoAccesoColaboradorResponse>,
    IRequestHandler<ReenviarAccesoColaboradorCommand, EstadoAccesoColaboradorResponse>
{
    private readonly IdentidadDbContext _identidad;
    private readonly CompartidoDbContext _compartido;
    private readonly IEntraDirectorioPort _directorio;
    private readonly ICorreoSalientePort _correoSaliente;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _entraOptions;
    private readonly IClock _clock;
    private readonly ILogger<AccesoColaboradorHandlers> _logger;

    public AccesoColaboradorHandlers(
        IdentidadDbContext identidad,
        CompartidoDbContext compartido,
        IEntraDirectorioPort directorio,
        ICorreoSalientePort correoSaliente,
        IOptionsMonitor<EntraDirectorioOptions> entraOptions,
        IClock clock,
        ILogger<AccesoColaboradorHandlers> logger)
    {
        _identidad = identidad;
        _compartido = compartido;
        _directorio = directorio;
        _correoSaliente = correoSaliente;
        _entraOptions = entraOptions;
        _clock = clock;
        _logger = logger;
    }

    public async Task<EstadoAccesoColaboradorResponse> Handle(
        ObtenerAccesoColaboradorQuery query, CancellationToken cancellationToken)
    {
        var (empleado, usuario) = await CargarAsync(query.EmpleadoId, tracking: false, cancellationToken);
        return Respuesta(empleado, usuario);
    }

    public async Task<EstadoAccesoColaboradorResponse> Handle(
        ReintentarProvisionColaboradorCommand command, CancellationToken cancellationToken)
    {
        var (empleado, usuario) = await CargarAsync(command.EmpleadoId, tracking: true, cancellationToken);
        ValidarActivos(empleado, usuario);
        usuario.ReintentarProvision();
        await _identidad.SaveChangesAsync(cancellationToken);
        return Respuesta(empleado, usuario);
    }

    public async Task<EstadoAccesoColaboradorResponse> Handle(
        ReenviarAccesoColaboradorCommand command, CancellationToken cancellationToken)
    {
        var (empleado, usuario) = await CargarAsync(command.EmpleadoId, tracking: true, cancellationToken);
        ValidarActivos(empleado, usuario);
        if (usuario.TieneOidPendiente || usuario.EstadoAcceso is not EstadoAcceso.PendientePrimerAcceso)
        {
            throw new BusinessRuleException(
                "USUARIO_ACCESO_NO_REENVIABLE",
                "Solo se reenvía el acceso a una cuenta creada en Entra que aún no inicia sesión.");
        }
        if (string.IsNullOrWhiteSpace(empleado.EmailContacto))
        {
            throw new BusinessRuleException(
                "COLABORADOR_SIN_CORREO_CONTACTO",
                "El empleado no tiene correo de contacto para enviarle el acceso.");
        }

        // Primero Entra y el correo; si cualquiera falla no se registra el envío.
        var contrasena = await _directorio.RestablecerContrasenaTemporalAsync(usuario.EntraOid, cancellationToken);
        await _correoSaliente.EnviarAccesoColaboradorAsync(
            new CorreoAccesoColaborador(
                empleado.EmailContacto,
                empleado.Nombre,
                usuario.Email,
                contrasena,
                _entraOptions.CurrentValue.UrlInicioSesion),
            cancellationToken);

        usuario.RegistrarEnvioAcceso(_clock.UtcNow);
        await _identidad.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Acceso reenviado a {Upn} (usuario {UsuarioId}).", usuario.Email, usuario.Id);
        return Respuesta(empleado, usuario);
    }

    private sealed record EmpleadoAcceso(Guid Id, string Nombre, string? EmailContacto,
        Guid? UsuarioId, EstatusCatalogo Estatus);

    private static void ValidarActivos(EmpleadoAcceso empleado, Usuario usuario)
    {
        if (empleado.Estatus != EstatusCatalogo.Activo || !usuario.Activo)
            throw new BusinessRuleException("COLABORADOR_INACTIVO",
                "El empleado y su usuario deben estar activos para gestionar el acceso.");
    }

    private async Task<(EmpleadoAcceso Empleado, Usuario Usuario)> CargarAsync(
        Guid empleadoId, bool tracking, CancellationToken ct)
    {
        var empleado = await _compartido.Empleados.AsNoTracking()
            .Where(e => e.Id == empleadoId)
            .Select(e => new EmpleadoAcceso(e.Id, e.Nombre, e.EmailContacto, e.UsuarioId, e.Estatus))
            .FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException(
                "EMPLEADO_NO_ENCONTRADO", $"No existe el empleado '{empleadoId}'.");
        if (empleado.UsuarioId is not Guid usuarioId)
        {
            throw new EntityNotFoundException(
                "COLABORADOR_SIN_ACCESO", "El empleado no tiene acceso al ERP.");
        }

        var usuarios = tracking ? _identidad.Usuarios : _identidad.Usuarios.AsNoTracking();
        var usuario = await usuarios.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == usuarioId, ct)
            ?? throw new EntityNotFoundException(
                "COLABORADOR_SIN_ACCESO", "El usuario vinculado al empleado no existe.");
        return (empleado, usuario);
    }

    private static EstadoAccesoColaboradorResponse Respuesta(EmpleadoAcceso empleado, Usuario usuario) => new(
        empleado.Id,
        usuario.Id,
        usuario.Email,
        usuario.EstadoAcceso,
        usuario.MotivoErrorProvision,
        usuario.AccesoEnviadoEn,
        usuario.PrimerAccesoEn,
        empleado.EmailContacto,
        usuario.Activo);
}
