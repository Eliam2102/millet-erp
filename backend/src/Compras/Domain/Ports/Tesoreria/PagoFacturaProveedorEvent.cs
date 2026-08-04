using MediatR;

namespace Millet.Compras.Domain.Ports.Tesoreria;

/// <summary>
/// Evento emitido por Tesorería cuando aplica un pago a una factura de
/// proveedor asociada a una OC (F5-PR2). El listener
/// <c>PagoFacturaProveedorListener</c> invoca
/// <see cref="Oc.OrdenCompra.RegistrarPago"/>.
///
/// <para>
/// El sub-estado de pago se deriva a nivel cabecera: el evento trae el
/// acumulado total pagado contra la OC (suma de pagos aplicados a sus
/// facturas). Tesorería resuelve la agregación.
/// </para>
/// </summary>
public sealed record PagoFacturaProveedorEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    decimal MontoPagadoAcumulado,
    DateTimeOffset OcurridoEn) : INotification;
