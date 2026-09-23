using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Infrastructure.Workers;

/// <summary>
/// <c>BackgroundService</c> que recorre diariamente a las 9 AM los
/// vales con <c>pendiente_regularizacion=true</c> y aplica el SLA del
/// A14 (01-diseno §3):
/// <list type="bullet">
///   <item><b>Día 1</b> tras vencer la fecha límite (+48h): notifica
///   al Coordinador.</item>
///   <item><b>Día 2</b>: notifica al Jefe de Almacén.</item>
/// </list>
///
/// <para>
/// Sin bloqueo automático del vale (A14). El operador resuelve
/// manualmente vía <c>RegularizarSalidaPorValeCommand</c>.
/// </para>
///
/// <para>
/// En F5-PR1 el servicio de notificación es <see cref="NoOpNotificacionService"/>
/// (log-only); el integrador real con email/SignalR entra cuando el
/// módulo Notificaciones (ADR-0026) exista —
/// <c>PLATFORM-TODO(&lt;NotificacionService&gt;)</c>.
/// </para>
/// </summary>
public sealed class RegularizacionValeSlaWorker : BackgroundService
{
    /// <summary>Hora del día (UTC) a la que arranca el sweep.</summary>
    private static readonly TimeSpan SweepHoraDia = TimeSpan.FromHours(15); // 9am MX = 15 UTC

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegularizacionValeSlaWorker> _logger;

    public RegularizacionValeSlaWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<RegularizacionValeSlaWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RegularizacionValeSlaWorker iniciado. Sweep diario a las {Hora} UTC.",
            SweepHoraDia);

        while (!stoppingToken.IsCancellationRequested)
        {
            var ahora = DateTimeOffset.UtcNow;
            var hoyTarget = new DateTimeOffset(
                ahora.Year, ahora.Month, ahora.Day, 0, 0, 0, TimeSpan.Zero) + SweepHoraDia;
            var siguiente = ahora > hoyTarget
                ? hoyTarget.AddDays(1)
                : hoyTarget;
            var espera = siguiente - ahora;

            try
            {
                await Task.Delay(espera, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await EjecutarSweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RegularizacionValeSlaWorker sweep falló.");
            }
        }
    }

    /// <summary>Internal para tests sin esperar al timer.</summary>
    internal async Task EjecutarSweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AlmacenDbContext>();
        var notif = sp.GetRequiredService<INotificacionService>();
        var originContext = sp.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(RegularizacionValeSlaWorker));
        var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var ahora = DateTimeOffset.UtcNow;
        var vales = await db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.SalidaPorVale
                && m.PendienteRegularizacion
                && m.Estado == EstadoMovimiento.Registrado
                && m.FechaLimiteRegularizacion < ahora)
            .ToListAsync(cancellationToken);

        if (vales.Count == 0)
        {
            _logger.LogInformation("Sweep: 0 vales vencidos.");
            return;
        }

        var notificados = 0;
        foreach (var vale in vales)
        {
            var horasVencidas = (ahora - vale.FechaLimiteRegularizacion!.Value).TotalHours;
            // Día 1: 0–24h tras vencer. Día 2: 24–48h. >48h: continúa notificando día 2.
            var diaDelSla = horasVencidas <= 24 ? 1 : 2;

            await notif.NotificarValeSinRegularizarAsync(
                movimientoValeId: vale.Id,
                folioVale: vale.Folio ?? "(s/folio)",
                personaDestinatariaId: vale.PersonaDestinatariaId,
                fechaLimite: vale.FechaLimiteRegularizacion!.Value,
                diaDelSla: diaDelSla,
                cancellationToken: cancellationToken);
            notificados++;
        }

        _logger.LogInformation(
            "Sweep RegularizacionValeSla: notificados {N} vales vencidos.", notificados);
    }
}
