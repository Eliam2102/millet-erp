using MediatR;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;

/// <summary>
/// Evento in-process emitido cuando un pago de Tesorería se aplica (o
/// se revierte) sobre una <see cref="FacturaProveedor"/> y su
/// <c>ImportePagado</c> cambia. Cierra el PLATFORM-TODO
/// <c>&lt;TesoreriaEventListenerCompras&gt;</c>: en lugar de que Compras
/// mantenga una proyección local de facturas, CxP — dueño del dato —
/// publica el acumulado pagado por OC ya calculado (patrón "acumulados
/// calculados por el publisher" de la triada) vía
/// <c>FacturaPagoAplicadoMapper</c>.
///
/// <para>
/// <paramref name="ImportePagadoFactura"/> es el importe pagado TOTAL de
/// la factura DESPUÉS de aplicar/revertir el pago (no el delta). El
/// mapper lo suma con el de las demás facturas de la misma OC para
/// producir el acumulado que Compras aplica con
/// <c>OrdenCompra.RegistrarPago</c>.
/// </para>
/// </summary>
public sealed record FacturaProveedorPagoAplicadoDomainEvent(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid? OrdenCompraId,
    decimal ImportePagadoFactura,
    DateTimeOffset OcurridoEn) : INotification;
