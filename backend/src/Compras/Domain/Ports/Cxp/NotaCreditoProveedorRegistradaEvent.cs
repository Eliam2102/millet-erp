using MediatR;

namespace Millet.Compras.Domain.Ports.Cxp;

/// <summary>
/// Evento emitido por CxP cuando registra una nota de crédito de
/// proveedor que reduce el acumulado facturado de una línea de OC
/// (F5-PR2). Análogo a <see cref="FacturaProveedorRegistradaEvent"/>
/// pero modela explícitamente la reducción.
///
/// <para>
/// <see cref="CantidadFacturadaAcumuladaAjustada"/> es el nuevo
/// acumulado tras aplicar la nota de crédito.
/// </para>
/// </summary>
public sealed record NotaCreditoProveedorRegistradaEvent(
    Guid OrdenCompraId,
    Guid LineaOrdenCompraId,
    Guid EmpresaId,
    decimal CantidadFacturadaAcumuladaAjustada,
    DateTimeOffset OcurridoEn) : INotification;
