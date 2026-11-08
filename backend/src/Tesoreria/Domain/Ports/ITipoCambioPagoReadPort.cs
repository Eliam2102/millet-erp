namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// TES-09 (86e3jvvj4) · puerto de CONSUMIDOR: tipo de cambio del catálogo (K10.6, <c>compartido.tipos_cambio</c>) a la fecha del
/// pago o del cobro, cuando no se captura el que aplicó el banco. Lo implementa un adapter sobre Compartido.
/// </summary>
public interface ITipoCambioPagoReadPort
{
    /// <summary>Pesos por una unidad de <paramref name="moneda"/> en <paramref name="fecha"/>; MXN = 1; null si el catálogo no lo tiene.</summary>
    Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken ct);
}
