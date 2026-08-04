using MediatR;

namespace Millet.Compras.Domain.Ports.Cxp;

/// <summary>
/// Evento emitido por CxP cuando registra una factura de proveedor
/// contra una línea de OC (F5-PR2). El listener
/// <c>FacturaProveedorRegistradaListener</c> invoca
/// <see cref="Oc.OrdenCompra.RegistrarFacturacionLinea"/>.
///
/// <para>
/// <b>Idempotencia</b>: <see cref="CantidadFacturadaAcumulada"/> es el
/// acumulado (set), no el delta. CxP es responsable de sumar todas las
/// facturas de la línea antes de publicar.
/// </para>
/// </summary>
public sealed record FacturaProveedorRegistradaEvent(
    Guid OrdenCompraId,
    Guid LineaOrdenCompraId,
    Guid EmpresaId,
    decimal CantidadFacturadaAcumulada,
    DateTimeOffset OcurridoEn) : INotification;
