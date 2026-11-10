using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Administracion.Domain;
using Millet.Compartido.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.TipoCambio;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Correo;

namespace Millet.Integraciones.Aw.Infrastructure.TipoCambio.Workers;

/// <summary>
/// Worker en segundo plano para la publicación periódica y supervisión de tipo de cambio hacia A+W (I13.2 / CA13.9).
/// Ejecuta <see cref="AwTipoCambioPublicador.PublicarAsync"/> y evalúa tres alertas operativas:
/// 1. TC de mañana sin publicar a las 18:00 (I13.2-a).
/// 2. A+W no aplicó el TC de hoy a la hora límite (08:00) o lo rechazó (I13.2-e).
/// 3. Desfase entre el TC vigente en A+W (sysadm.ka_waehrungen) y el publicado por ERP (CA13.9).
/// Control de frecuencia: emisión máxima de 1 alerta por tipo al día.
/// La alerta 2 y la publicación fallida después de la hora límite también salen por correo a los destinatarios del
/// parámetro <see cref="AvisoTipoCambioAwParametro"/> (editable en Administración → Parámetros, sin desplegar).
/// </summary>
public sealed class AwTipoCambioWorker : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AwTipoCambioOptions> _options;
    private readonly ILogger<AwTipoCambioWorker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<(string Tipo, DateOnly Fecha, string Moneda)> _alertasEmitidas = [];
    private readonly HashSet<(string Tipo, DateOnly Fecha, string Moneda)> _avisosEnviados = [];

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

        // Limpiar marcas antiguas de alertas para prevenir acumulación indefinida
        _alertasEmitidas.RemoveWhere(a => a.Fecha < hoy.AddDays(-2));
        _avisosEnviados.RemoveWhere(a => a.Fecha < hoy.AddDays(-2));

        var limitePublicacion = TimeSpan.TryParse(opts.HoraLimitePublicacion, CultureInfo.InvariantCulture, out var lp)
            ? lp
            : new TimeSpan(18, 0, 0);

        var limiteAplicacion = TimeSpan.TryParse(opts.HoraLimiteAplicacion, CultureInfo.InvariantCulture, out var la)
            ? la
            : new TimeSpan(8, 0, 0);

        try
        {
            await publicador.PublicarAsync(hoy, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "ALERTA_TC_PUBLICACION_FALLIDA: no se pudo publicar el tipo de cambio de {Fecha} hacia A+W.",
                hoy.ToString("yyyy-MM-dd"));
            // Antes de la hora límite el siguiente ciclo reintenta; después, TI corre su tarea a mano hoy.
            if (ahora.TimeOfDay >= limiteAplicacion)
            {
                await AvisarPorCorreoAsync(sp, hoy,
                    [new AvisoTc(("PUBLICACION", hoy, "*"), "Todas", null, $"La publicación del ERP falló: {ex.Message}")], ct);
            }
            return;
        }

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

        var acuses = await port.ObtenerUltimosAcusesAsync(monedas, hoy, ct);
        var acusesPorMonedaYFecha = acuses
            .GroupBy(a => (a.Moneda.Trim().ToUpperInvariant(), a.FechaAplicacion))
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.Version)!);

        var avisos = new List<AvisoTc>();

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

            // Alerta 2 (I13.2-e) y Alerta 3 (CA13.9): Si ahora.TimeOfDay >= HoraLimiteAplicacion (08:00)
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

                    var error = acuseHoy is null
                        ? "No hay fila publicada para hoy en dbo.erp_tipo_cambio."
                        : resultado == 2
                            ? $"A+W rechazó la fila: {acuseHoy.Motivo ?? "sin motivo"} (intentos: {intentos})."
                            : "A+W no ha aplicado la fila (sin acuse).";
                    avisos.Add(new AvisoTc(("ALERTA_2", hoy, moneda), moneda, acuseHoy?.Valor, error));
                }
                else if (resultado == 1 && acuseHoy is not null)
                {
                    var awValor = await port.ObtenerTipoCambioVigenteAwAsync(moneda, ct);
                    var erpValor = acuseHoy.Valor;

                    // VALUTAKURS es el inverso del valor ERP (MXN por unidad): se compara en el dominio de A+W.
                    if (awValor != AwTipoCambioAdapter.ValutakursDe(erpValor))
                    {
                        if (_alertasEmitidas.Add(("ALERTA_3", hoy, moneda)))
                        {
                            _logger.LogWarning(
                                "ALERTA_TC_DESFASE_ERP_AW: Discrepancia en tipo de cambio para {Moneda}: ERP={ErpValor} (A+W esperado={AwEsperado}), A+W={AwValor}.",
                                moneda,
                                erpValor,
                                AwTipoCambioAdapter.ValutakursDe(erpValor),
                                awValor);
                        }
                    }
                }
            }
        }

        if (avisos.Count > 0)
        {
            await AvisarPorCorreoAsync(sp, hoy, avisos, ct);
        }
    }

    private sealed record AvisoTc((string Tipo, DateOnly Fecha, string Moneda) Clave, string Moneda, decimal? Valor, string Error);

    /// <summary>
    /// TI de Millet (Jorge Toache, 10-nov): si después de la hora límite el TC llega tarde o falla, corren su tarea a mano
    /// ese día. Un correo por ciclo con todo lo pendiente; lo ya avisado no se repite ese día. Si el correo no sale, se
    /// reintenta en el siguiente ciclo.
    /// </summary>
    private async Task AvisarPorCorreoAsync(IServiceProvider sp, DateOnly hoy, IReadOnlyList<AvisoTc> avisos, CancellationToken ct)
    {
        var pendientes = avisos.Where(a => !_avisosEnviados.Contains(a.Clave)).ToList();
        var correo = sp.GetService<ICorreoSaliente>();
        var db = sp.GetService<CompartidoDbContext>();
        if (pendientes.Count == 0 || correo is null || db is null)
        {
            return;
        }

        try
        {
            var valor = await db.ParametrosGlobales.AsNoTracking()
                .Where(p => p.Clave == AvisoTipoCambioAwParametro.Clave)
                .Select(p => p.Valor)
                .SingleOrDefaultAsync(ct);
            var para = AvisoTipoCambioAwParametro.LeerCorreos(valor ?? string.Empty);
            if (para.Count == 0)
            {
                _logger.LogWarning("AwTipoCambioWorker: el parámetro {Clave} no tiene correos; el aviso solo queda en el log.",
                    AvisoTipoCambioAwParametro.Clave);
                return;
            }

            // Sin fila publicada, el valor sale del catálogo del ERP para que TI lo capture a mano.
            var sinValor = pendientes.Where(a => a.Valor is null && a.Clave.Moneda != "*").Select(a => a.Moneda).ToList();
            var catalogo = new Dictionary<string, decimal>();
            if (sinValor.Count > 0 && sp.GetService<ITipoCambioRangoReadPort>() is { } rango)
            {
                foreach (var item in await rango.ObtenerRangoTipoCambioAsync(sinValor, hoy, hoy, ct))
                {
                    catalogo.TryAdd(item.Moneda.Trim().ToUpperInvariant(), item.ValorEnMxn);
                }
            }

            var filas = string.Concat(pendientes.Select(a =>
            {
                var v = a.Valor ?? (catalogo.TryGetValue(a.Moneda, out var c) ? c : null);
                var textoValor = v is null ? "sin valor en el ERP" : v.Value.ToString("0.######", CultureInfo.InvariantCulture);
                return $"<tr><td>{hoy:yyyy-MM-dd}</td><td>{WebUtility.HtmlEncode(a.Moneda)}</td><td>{textoValor}</td><td>{WebUtility.HtmlEncode(a.Error)}</td></tr>";
            }));
            var html = $"<p>El tipo de cambio de hoy ({hoy:yyyy-MM-dd}) no quedó aplicado en A+W a la hora límite.</p>"
                + "<p><strong>Por favor corran la tarea a mano hoy.</strong></p>"
                + "<table border=\"1\" cellpadding=\"4\" cellspacing=\"0\"><tr><th>Fecha</th><th>Moneda</th><th>Valor (MXN)</th><th>Error</th></tr>"
                + filas + "</table><p>Aviso automático del ERP Millet (I13.2).</p>";

            var resultado = await correo.EnviarAsync(new MensajeCorreo(null, "integraciones.aw", "aviso-tipo-cambio-aw", null, para,
                $"ERP Millet: tipo de cambio {hoy:yyyy-MM-dd} sin aplicar en A+W, corran la tarea a mano hoy", html), ct);
            if (resultado.Estado == EstadoCorreoSaliente.Simulado)
            {
                // Proveedor NoOp: no salió ningún correo; no se da por avisado.
                _logger.LogWarning("AwTipoCambioWorker: el correo está simulado (NoOp); el aviso del tipo de cambio no salió y se reintenta en el siguiente ciclo.");
                return;
            }

            if (resultado.Estado == EstadoCorreoSaliente.Error)
            {
                if (!EsErrorPermanente(resultado.Codigo))
                {
                    _logger.LogWarning("AwTipoCambioWorker: el aviso por correo no salió ({Codigo}: {Motivo}); se reintenta en el siguiente ciclo.",
                        resultado.Codigo, resultado.Motivo);
                    return;
                }

                // Reintentar no lo arregla (permisos, destinatarios): se registra una vez y el aviso de hoy se da por atendido.
                _logger.LogError("AwTipoCambioWorker: el aviso por correo no salió y no se reintenta ({Codigo}: {Motivo}); revisar la configuración del correo o el parámetro {Clave}.",
                    resultado.Codigo, resultado.Motivo, AvisoTipoCambioAwParametro.Clave);
            }

            _avisosEnviados.UnionWith(pendientes.Select(a => a.Clave));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "AwTipoCambioWorker: no se pudo enviar el aviso por correo; se reintenta en el siguiente ciclo.");
        }
    }

    /// <summary>Errores que no se arreglan reintentando; los límites del proveedor y las fallas de envío sí se reintentan.</summary>
    private static bool EsErrorPermanente(string? codigo) => codigo is CodigosCorreoSaliente.PermisosInsuficientes
        or CodigosCorreoSaliente.ExcedeDestinatarios or CodigosCorreoSaliente.SinDestinatarios or CodigosCorreoSaliente.AdjuntosExceden3Mb;
}
