using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Conciliacion;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Infrastructure.Workers;

public sealed class ConciliacionCarteraOptions
{
    public const string SectionName = "CuentasPorCobrar:Conciliacion";

    public bool Disabled { get; set; }

    /// <summary>
    /// Hora de Millet a partir de la cual se concilia el día anterior. Supuesto a
    /// validar: 06:00, antes de que CxC empiece a cobrar o a liberar pedidos.
    /// </summary>
    public int HoraLocal { get; set; } = 6;
}

/// <summary>
/// C7.7: concilia cada día el día contable anterior contra Facturación. Revisa cada
/// 15 minutos; corre una sola vez por día (advisory lock + «ya hay corrida
/// programada de esa fecha»), así que un reinicio después de la hora no la pierde.
/// </summary>
public sealed class ConciliacionCarteraWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ConciliacionCarteraOptions> options,
    ILogger<ConciliacionCarteraWorker> logger) : BackgroundService
{
    private const long AdvisoryLockId = 7_390_077L;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    /// <summary>Día a conciliar si ya pasó la hora configurada; null si todavía no toca.</summary>
    public static DateOnly? DiaPendiente(DateTimeOffset utc, int horaLocal)
    {
        var local = DiaContableCxc.Ahora(utc);
        return local.Hour >= Math.Clamp(horaLocal, 0, 23)
            ? DateOnly.FromDateTime(local.DateTime).AddDays(-1)
            : null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Value.Disabled)
        {
            logger.LogInformation("ConciliacionCarteraWorker deshabilitado por configuración.");
            return;
        }

        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CorrerSiTocaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ConciliacionCarteraWorker: error al conciliar — reintenta en el siguiente ciclo.");
            }

            try { await Task.Delay(Intervalo, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task CorrerSiTocaAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        if (DiaPendiente(sp.GetRequiredService<IClock>().UtcNow, options.Value.HoraLocal) is not DateOnly dia)
            return;

        using var origin = sp.GetRequiredService<IAuditOriginContext>().SetOrigin(nameof(ConciliacionCarteraWorker));
        using var bypass = sp.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = sp.GetRequiredService<CuentasPorCobrarDbContext>();

        await PostgresAdvisoryLock.ExecuteAsync(db, AdvisoryLockId, async token =>
        {
            if (await db.ConciliacionesCartera.AnyAsync(c => c.Fecha == dia && c.Origen == OrigenConciliacion.Programada, token))
                return;
            var corridas = await sp.GetRequiredService<IMediator>()
                .Send(new ConciliarCarteraCommand(dia, OrigenConciliacion.Programada), token);
            logger.LogInformation("ConciliacionCarteraWorker: día {Dia} conciliado ({Corridas} corrida(s), {Diferencias} diferencia(s)).",
                dia, corridas.Count, corridas.Sum(c => c.TotalDiferencias));
        }, ct);
    }
}
