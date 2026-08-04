using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;

namespace Millet.CuentasPorPagar.Infrastructure.Stubs;

/// <summary>
/// Stub de <see cref="ITipoCambioReadPort"/> (F0-PR1). Devuelve 1.0
/// para MXN→MXN; <c>null</c> en cualquier otra combinación (el handler
/// degrada a validación manual).
///
/// PLATFORM-TODO(&lt;TipoCambioReadPort&gt;): adapter real con histórico
/// cuando Catálogos / Administración expongan <c>tipos_cambio</c>.
/// </summary>
public sealed class NoOpTipoCambioReadPort : ITipoCambioReadPort
{
    private readonly ILogger<NoOpTipoCambioReadPort> _logger;

    public NoOpTipoCambioReadPort(ILogger<NoOpTipoCambioReadPort> logger)
    {
        _logger = logger;
    }

    public Task<decimal?> ObtenerAsync(
        string monedaOrigen,
        string monedaDestino,
        DateOnly fecha,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "[NoOp] ITipoCambioReadPort.ObtenerAsync origen={Origen} destino={Destino} fecha={Fecha}",
            monedaOrigen, monedaDestino, fecha);

        if (string.Equals(monedaOrigen, monedaDestino, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<decimal?>(1m);
        }

        return Task.FromResult<decimal?>(null);
    }
}
