using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Compartido.Domain.Ports;

namespace Millet.Integraciones.Aw.Application.TipoCambio;

/// <summary>
/// Publicador del tipo de cambio oficial del ERP hacia la tabla puente dbo.erp_tipo_cambio (I13.2).
/// Coordina la lectura desde <see cref="ITipoCambioRangoReadPort"/> y la publicación idempotente
/// mediante <see cref="IAwTipoCambioPort"/>.
/// </summary>
public sealed class AwTipoCambioPublicador(
    ITipoCambioRangoReadPort rangoReadPort,
    IAwTipoCambioPort awTipoCambioPort,
    IOptions<AwTipoCambioOptions> options,
    ILogger<AwTipoCambioPublicador> logger)
{
    private readonly ITipoCambioRangoReadPort _rangoReadPort = rangoReadPort;
    private readonly IAwTipoCambioPort _awTipoCambioPort = awTipoCambioPort;
    private readonly AwTipoCambioOptions _options = options.Value;
    private readonly ILogger<AwTipoCambioPublicador> _logger = logger;

    /// <summary>
    /// Ejecuta un ciclo de publicación hacia la tabla puente dbo.erp_tipo_cambio para el rango
    /// desde [fechaReferencia - 7 días] hasta [fechaReferencia + 4 días].
    /// </summary>
    public async Task<ResultadoPublicacionAw> PublicarAsync(DateOnly fechaReferencia, CancellationToken ct = default)
    {
        if (!_options.Habilitado)
        {
            _logger.LogInformation("Publicación de tipo de cambio hacia A+W deshabilitada por configuración.");
            return new ResultadoPublicacionAw(0, 0, false, "Canal deshabilitado por configuración");
        }

        var monedasConfiguradas = _options.Monedas ?? ["USD", "EUR", "CAD"];
        var monedas = monedasConfiguradas
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        if (monedas.Count == 0)
        {
            return new ResultadoPublicacionAw(0, 0, true, "Sin monedas configuradas");
        }

        var desde = fechaReferencia.AddDays(-7);
        var hasta = fechaReferencia.AddDays(4);

        var catalogoItems = await _rangoReadPort.ObtenerRangoTipoCambioAsync(monedas, desde, hasta, ct);
        var acuses = await _awTipoCambioPort.ObtenerUltimosAcusesAsync(monedas, desde, ct);

        // Agrupar acuses existentes por (Moneda, FechaAplicacion) quedándonos con la versión mayor
        var acusesPorMonedaYFecha = acuses
            .GroupBy(a => (a.Moneda.Trim().ToUpperInvariant(), a.FechaAplicacion))
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.Version)!);

        int insertadas = 0;
        int omitidas = 0;

        foreach (var item in catalogoItems)
        {
            var monedaNormalizada = item.Moneda.Trim().ToUpperInvariant();
            var key = (monedaNormalizada, item.Fecha);

            if (acusesPorMonedaYFecha.TryGetValue(key, out var ultimoAcuse))
            {
                if (ultimoAcuse.Valor == item.ValorEnMxn)
                {
                    omitidas++;
                    continue;
                }

                var nuevaVersion = ultimoAcuse.Version + 1;
                var fila = new AwTipoCambioFila(
                    monedaNormalizada,
                    item.Fecha,
                    nuevaVersion,
                    item.ValorEnMxn,
                    null,
                    item.Origen ?? "Banxico");

                await _awTipoCambioPort.PublicarFilaAsync(fila, ct);
                acusesPorMonedaYFecha[key] = new AwTipoCambioAcuse(
                    fila.Moneda,
                    fila.FechaAplicacion,
                    fila.Version,
                    fila.Valor,
                    null,
                    null,
                    null,
                    0);
                insertadas++;
            }
            else
            {
                var fila = new AwTipoCambioFila(
                    monedaNormalizada,
                    item.Fecha,
                    1,
                    item.ValorEnMxn,
                    null,
                    item.Origen ?? "Banxico");

                await _awTipoCambioPort.PublicarFilaAsync(fila, ct);
                acusesPorMonedaYFecha[key] = new AwTipoCambioAcuse(
                    fila.Moneda,
                    fila.FechaAplicacion,
                    fila.Version,
                    fila.Valor,
                    null,
                    null,
                    null,
                    0);
                insertadas++;
            }
        }

        var mensaje = $"Publicación exitosa: {insertadas} insertadas, {omitidas} omitidas";
        _logger.LogInformation(
            "Publicación de tipo de cambio hacia A+W finalizada: {Insertadas} insertadas, {Omitidas} omitidas",
            insertadas, omitidas);

        return new ResultadoPublicacionAw(insertadas, omitidas, true, mensaje);
    }
}
