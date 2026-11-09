using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.Infrastructure.TipoCambio.Workers;

/// <summary>
/// Worker en segundo plano para la publicación periódica y supervisión de tipo de cambio hacia A+W (I13.2 / CA13.9).
/// Ejecuta <see cref="AwTipoCambioPublicador.PublicarAsync"/> y evalúa tres alertas operativas:
/// 1. TC de mañana sin publicar a las 18:00 (I13.2-a).
/// 2. A+W no aplicó el TC de hoy a las 09:00 o rechazos reiterados (I13.2-e).
/// 3. Desfase entre el TC vigente en A+W (sysadm.ka_waehrungen) y el publicado por ERP (CA13.9).
/// Control de frecuencia: emisión máxima de 1 alerta por tipo al día.
/// </summary>
public sealed class AwTipoCambioWorker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AwTipoCambioOptions> _options;
    private readonly ILogger<AwTipoCambioWorker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<(string Tipo, DateOnly Fecha, string Moneda)> _alertasEmitidas = [];

    public AwTipoCambioWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AwTipoCambioOptions> options,
        ILogger<AwTipoCambioWorker> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var ahora = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), FechaContable.ZonaOperacion);
                await EjecutarCicloAsync(ahora, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AwTipoCambioWorker: el ciclo de ejecución falló; se reintentará en el siguiente intervalo.");
            }

            try
            {
                await Task.Delay(Intervalo, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task EjecutarCicloAsync(DateTimeOffset ahora, CancellationToken ct = default)
    {
        var opts = _options.Value;
        if (!opts.Habilitado)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        using var origin = sp.GetService<IAuditOriginContext>()?.SetOrigin(nameof(AwTipoCambioWorker));

        var publicador = sp.GetRequiredService<AwTipoCambioPublicador>();
        var port = sp.GetRequiredService<IAwTipoCambioPort>();

        var hoy = DateOnly.FromDateTime(ahora.DateTime);
        var manana = hoy.AddDays(1);

        await publicador.PublicarAsync(hoy, ct);

        var monedasConfiguradas = opts.Monedas ?? ["USD", "EUR", "CAD"];
        var monedas = monedasConfiguradas
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (monedas.Count == 0)
        {
            return;
        }

        // Limpiar marcas antiguas de alertas para prevenir acumulación indefinida
        _alertasEmitidas.RemoveWhere(a => a.Fecha < hoy.AddDays(-2));

        var limitePublicacion = TimeSpan.TryParse(opts.HoraLimitePublicacion, CultureInfo.InvariantCulture, out var lp)
            ? lp
            : new TimeSpan(18, 0, 0);

        var limiteAplicacion = TimeSpan.TryParse(opts.HoraLimiteAplicacion, CultureInfo.InvariantCulture, out var la)
            ? la
            : new TimeSpan(9, 0, 0);

        var acuses = await port.ObtenerUltimosAcusesAsync(monedas, hoy, ct);
        var acusesPorMonedaYFecha = acuses
            .GroupBy(a => (a.Moneda.Trim().ToUpperInvariant(), a.FechaAplicacion))
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.Version)!);

        foreach (var moneda in monedas)
        {
            // Alerta 1 (I13.2-a): Si ahora.TimeOfDay >= HoraLimitePublicacion (18:00)
            if (ahora.TimeOfDay >= limitePublicacion)
            {
                var keyManana = (moneda, manana);
                if (!acusesPorMonedaYFecha.ContainsKey(keyManana))
                {
                    if (_alertasEmitidas.Add(("ALERTA_1", hoy, moneda)))
                    {
                        _logger.LogWarning(
                            "ALERTA_TC_MANANA_SIN_PUBLICAR: Tipo de cambio para mañana ({Fecha}) no publicado a las 18:00 para {Moneda}. A+W conservará el último TC.",
                            manana.ToString("yyyy-MM-dd"),
                            moneda);
                    }
                }
            }

            // Alerta 2 (I13.2-e) y Alerta 3 (CA13.9): Si ahora.TimeOfDay >= HoraLimiteAplicacion (09:00)
            if (ahora.TimeOfDay >= limiteAplicacion)
            {
                var keyHoy = (moneda, hoy);
                acusesPorMonedaYFecha.TryGetValue(keyHoy, out var acuseHoy);

                var resultado = acuseHoy?.Resultado;
                var intentos = acuseHoy?.Intentos ?? 0;

                if (resultado != 1 || (resultado == 2 && intentos >= 3))
                {
                    if (_alertasEmitidas.Add(("ALERTA_2", hoy, moneda)))
                    {
                        _logger.LogWarning(
                            "ALERTA_TC_NO_APLICADO_AW: A+W no ha aplicado el tipo de cambio de hoy ({Fecha}) para {Moneda}. Resultado: {Resultado}, Intentos: {Intentos}.",
                            hoy.ToString("yyyy-MM-dd"),
                            moneda,
                            resultado,
                            intentos);
                    }
                }
                else if (resultado == 1 && acuseHoy is not null)
                {
                    var awValor = await port.ObtenerTipoCambioVigenteAwAsync(moneda, ct);
                    var erpValor = acuseHoy.Valor;

                    if (awValor != erpValor)
                    {
                        if (_alertasEmitidas.Add(("ALERTA_3", hoy, moneda)))
                        {
                            _logger.LogWarning(
                                "ALERTA_TC_DESFASE_ERP_AW: Discrepancia en tipo de cambio para {Moneda}: ERP={ErpValor}, A+W={AwValor}.",
                                moneda,
                                erpValor,
                                awValor);
                        }
                    }
                }
            }
        }
    }
}
