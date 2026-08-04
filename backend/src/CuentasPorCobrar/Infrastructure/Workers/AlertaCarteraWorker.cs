using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.Infrastructure.Workers;

/// <summary>
/// Worker de evaluación programada de alertas de cartera (CXC-PR8,
/// 01-diseño §9): SOLUNION 90d, exceso de crédito y auto-bloqueo por
/// vencimientos. Corre cada <c>IntervalHours</c> (default 24) y delega
/// la lógica a <see cref="EvaluarAlertasCarteraCommand"/> — el command
/// es lo que se prueba; el worker solo agenda.
///
/// <para><b>Empresa bypass</b>: corre fuera de un request HTTP; el
/// query filter multi-tenant se levanta y la evaluación agrupa por la
/// EmpresaId de cada fila.</para>
/// </summary>
public sealed class AlertaCarteraWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertasCarteraOptions _options;
    private readonly ILogger<AlertaCarteraWorker> _logger;

    public AlertaCarteraWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AlertasCarteraOptions> options,
        ILogger<AlertaCarteraWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Disabled)
        {
            _logger.LogInformation("AlertaCarteraWorker deshabilitado por configuración.");
            return;
        }

        var intervalo = TimeSpan.FromHours(Math.Max(1, _options.IntervalHours));

        // Primer corrida poco después del arranque (deja estabilizar el host).
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;
                var empresaContext = sp.GetRequiredService<ICurrentEmpresaContext>();
                using var bypass = empresaContext.Bypass();

                var mediator = sp.GetRequiredService<IMediator>();
                var creadas = await mediator.Send(new EvaluarAlertasCarteraCommand(), stoppingToken);
                _logger.LogInformation("AlertaCarteraWorker: corrida completada ({N} alertas nuevas).", creadas);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertaCarteraWorker: error en la evaluación — reintenta en la siguiente corrida.");
            }

            try { await Task.Delay(intervalo, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }
}
