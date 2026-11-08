namespace Millet.CuentasPorCobrar.Domain.Ports.Catalogos;

/// <summary>
/// V-CXC-11 (86e3jvv78): TC del catálogo K10.6 (<c>compartido.tipos_cambio</c>) para mostrar
/// el equivalente en pesos de la cartera en moneda extranjera. Es informativo: no cambia
/// saldos ni la moneda de la factura.
/// </summary>
public interface ITipoCambioCarteraReadPort
{
    /// <summary>Pesos por 1 unidad de <paramref name="moneda"/> en <paramref name="fecha"/>; MXN vale 1; null si el catálogo no tiene esa fecha.</summary>
    Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken);
}
