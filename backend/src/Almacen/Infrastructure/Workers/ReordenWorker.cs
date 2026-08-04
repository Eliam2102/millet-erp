using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Infrastructure.Workers;

/// <summary>
/// Worker del motor de reorden (ADR-0047 PR5.D). Molde
/// <c>IdempotencyKeysCleanupJob</c>: loop por intervalo + advisory lock PG (exclusión
/// entre réplicas) + scope por ciclo + bypass de empresa (job de fondo sin JWT). La
/// orquestación vive en <see cref="GenerarBorradoresReordenCommand"/> (invocado por
/// <c>IMediator</c>); este worker es solo el hosting.
///
/// <para><b>Dos interruptores, con precedencia:</b></para>
/// <list type="number">
///   <item><b>Kill-switch de infraestructura</b> —
///     <see cref="ReordenWorkerOptions.Disabled"/> (default <c>true</c>, opt-in por
///     ambiente). Si está apagado, el servicio termina al arrancar y NO consulta
///     nada más (ni la BD). Cambiarlo requiere config + reinicio, como siempre.</item>
///   <item><b>Interruptor operativo</b> — <c>AlmacenSettings.ReabastoAutomaticoActivo</c>
///     (BD, editable desde la pantalla de Reabasto). Se consulta EN CADA CICLO,
///     antes del advisory lock, para la empresa del usuario de servicio del motor
///     (<see cref="IUsuarioServicioReadPort"/>, mono-empresa). Apagado (default) →
///     el ciclo se salta sin lock, sin TX y sin command.</item>
/// </list>
///
/// <para><b>Semántica del toggle</b>: apagar NO cancela un barrido en curso (la TX
/// termina); surte efecto en el siguiente tick. Prender espera el próximo tick
/// (hasta <see cref="ReordenWorkerOptions.Interval"/>). Los borradores ya generados
/// quedan vivos como RQs normales.</para>
/// </summary>
public sealed class ReordenWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReordenWorkerOptions _options;
    private readonly ILogger<ReordenWorker> _logger;

    public ReordenWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ReordenWorkerOptions> options,
        ILogger<ReordenWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("ReordenWorker deshabilitado (ReordenWorker:Disabled=true).");
            return;
        }

        _logger.LogInformation("ReordenWorker activo. Intervalo: {Intervalo}.", _options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EjecutarBarridoAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ReordenWorker: el ciclo falló. Continúa tras el intervalo.");
            }

            try
            {
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Un ciclo completo del barrido. Internal para que los tests lo disparen sin
    /// esperar el timer. Consulta el interruptor operativo de BD (skip barato si
    /// está apagado), toma advisory lock (skip si otra réplica corre), bypass de
    /// empresa, y delega en <see cref="GenerarBorradoresReordenCommand"/>.
    /// </summary>
    internal async Task<GenerarBorradoresReordenResultado?> EjecutarBarridoAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var mediator = sp.GetRequiredService<IMediator>();
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        var db = sp.GetRequiredService<AlmacenDbContext>();

        // Bypass de empresa para todo el ciclo (job de fondo sin JWT): cubre tanto
        // la lectura del interruptor como el barrido. Es re-entrante con los bypass
        // internos de los adapters (5.B/5.C).
        using var bypass = empresaContext.Bypass();

        // Interruptor operativo (AlmacenSettings.ReabastoAutomaticoActivo) de la
        // empresa del usuario de servicio del motor — la misma identidad/empresa
        // con la que se crean las RQs. Se consulta ANTES del advisory lock y de la
        // TX: apagado (o usuario de servicio sin sembrar) → skip barato del ciclo.
        var usuarios = sp.GetRequiredService<IUsuarioServicioReadPort>();
        var usuarioServicio = await usuarios.ObtenerReordenAsync(ct);
        if (usuarioServicio is null)
        {
            _logger.LogDebug(
                "ReordenWorker: usuario de servicio del motor no sembrado. Skip este ciclo.");
            return null;
        }

        var activo = await db.AlmacenSettings
            .AsNoTracking()
            .Where(s => s.EmpresaId == usuarioServicio.EmpresaId)
            .Select(s => (bool?)s.ReabastoAutomaticoActivo)
            .FirstOrDefaultAsync(ct);
        if (activo is not true)
        {
            _logger.LogDebug(
                "ReordenWorker: reabasto automático apagado para la empresa {EmpresaId} " +
                "(AlmacenSettings.ReabastoAutomaticoActivo). Skip este ciclo.",
                usuarioServicio.EmpresaId);
            return null;
        }

        // Advisory lock tx-scoped: exclusión total entre réplicas (solo una crea RQs
        // por ciclo). Se libera al commit. El barrido corre dentro de esta TX.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database
            .SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"", _options.AdvisoryLockId)
            .SingleAsync(ct);
        if (!locked)
        {
            _logger.LogDebug("ReordenWorker: otra instancia ya corre el barrido. Skip este ciclo.");
            await tx.CommitAsync(ct);
            return null;
        }

        var resultado = await mediator.Send(new GenerarBorradoresReordenCommand(), ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "ReordenWorker: barrido OK — {Rqs} RQs ({Lineas} líneas), {Errores} errores.",
            resultado.RqsCreadas, resultado.LineasCreadas, resultado.Errores.Count);
        return resultado;
    }
}
