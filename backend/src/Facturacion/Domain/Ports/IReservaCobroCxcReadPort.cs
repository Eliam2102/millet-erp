namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// H-U41: importe de cada factura que Cuentas por Cobrar ya comprometió en propuestas de aplicación abiertas
/// (en revisión, o confirmadas por Tesorería sin REP timbrado). La caja lo descuenta del monto por cobrar para no
/// cobrar dos veces lo que ya entró por banco. Facturas sin reserva no aparecen en el diccionario.
/// </summary>
public interface IReservaCobroCxcReadPort
{
    Task<IReadOnlyDictionary<Guid, decimal>> ReservadoPorFacturaAsync(
        IReadOnlyCollection<Guid> facturaVentaIds, CancellationToken cancellationToken);
}
