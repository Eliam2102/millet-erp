namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// K10.7 · puerto que <b>Facturación expone</b>: de un grupo de facturas de venta, cuáles ya aparecen en un REP timbrado (complemento de
/// pago vigente). Lo consume Contabilidad para la marca de cumplimiento de la liquidación de IVA.
/// </summary>
public interface IRepEmitidoReadPort
{
    Task<IReadOnlyCollection<Guid>> FacturasConRepAsync(IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken ct);
}
