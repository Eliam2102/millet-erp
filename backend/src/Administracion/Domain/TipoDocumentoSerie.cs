namespace Millet.Administracion.Domain;

/// <summary>
/// Tipo de documento al que aplica una <see cref="Serie"/> de folios
/// (F-Admin-PR6.1). Empezamos con el mínimo cubierto por el MVP — el
/// resto de los valores se agregan cuando lleguen los módulos
/// (Facturación, CxP, Contabilidad).
///
/// <list type="bullet">
///   <item><see cref="OrdenCompra"/>: F-Admin-PR6.2 — Compras OC.</item>
///   <item><see cref="Cfdi"/>: Facturación — factura de venta (F1).</item>
///   <item><see cref="NotaCredito"/>: pendiente, Facturación.</item>
///   <item><see cref="Poliza"/>: pendiente, Contabilidad.</item>
///   <item><see cref="FacturaAnticipo"/>: Facturación — factura de anticipo,
///   serie dedicada (<c>FANT</c>, A8 del diseño; F4). Se separa de
///   <see cref="Cfdi"/> porque <c>ReservarFolioCommand</c> resuelve una sola
///   serie activa por (empresa, sucursal, tipo): el anticipo necesita su
///   propio prefijo para no consumir el folio de las facturas de venta.</item>
///   <item><see cref="VentaDirecta"/>: Facturación — serie CFDI dedicada a la
///   venta directa del ERP (pedidos manuales de venta directa o emergencia,
///   F6.1 / P6.1). Siempre <b>por sucursal</b> (sin serie global de respaldo);
///   mismo precedente que <see cref="FacturaAnticipo"/>: un prefijo propio para
///   no consumir el folio de las facturas de pedidos A+W.</item>
/// </list>
///
/// El valor numérico (<c>short</c>) está fijo por ABI — agregar nuevos
/// al final, nunca renumerar.
/// </summary>
public enum TipoDocumentoSerie : short
{
    OrdenCompra = 1,
    Cfdi = 2,
    NotaCredito = 3,
    Poliza = 4,
    FacturaAnticipo = 5,
    VentaDirecta = 6,
}
