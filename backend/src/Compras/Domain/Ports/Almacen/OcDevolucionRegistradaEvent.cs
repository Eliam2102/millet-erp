using MediatR;

namespace Millet.Compras.Domain.Ports.Almacen;

/// <summary>
/// Evento emitido por Almacén cuando se registra una devolución al
/// proveedor sobre una OC (F5-PR2). Decrementa el acumulado recibido
/// de la línea. Si la OC estaba <see cref="Oc.EstadoOrdenCompra.Cerrada"/>,
/// la transición de regreso a <see cref="Oc.EstadoOrdenCompra.Autorizada"/>
/// la dispara el listener al recalcular sub-estados.
///
/// <para>
/// <see cref="CantidadAcumuladaAjustada"/> es el nuevo acumulado (set),
/// no el delta. Por convención, el emisor calcula
/// <c>cantidadRecibidaAnterior - cantidadDevuelta</c> antes de publicar.
/// </para>
/// </summary>
public sealed record OcDevolucionRegistradaEvent(
    Guid OrdenCompraId,
    Guid LineaOrdenCompraId,
    Guid EmpresaId,
    decimal CantidadAcumuladaAjustada,
    DateTimeOffset OcurridoEn) : INotification;
