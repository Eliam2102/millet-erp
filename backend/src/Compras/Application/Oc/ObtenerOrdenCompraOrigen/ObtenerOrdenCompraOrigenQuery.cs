using MediatR;

namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraOrigen;

/// <summary>
/// Devuelve el id + folio de la OC origen desde la cual fue duplicada
/// la OC en cuestión (F6-PR2). Devuelve <c>null</c> si la OC no fue
/// creada vía <c>DuplicarOrdenCompraCommand</c> (campo
/// <c>oc_origen_id</c> NULL).
/// </summary>
public sealed record ObtenerOrdenCompraOrigenQuery(Guid OrdenCompraId)
    : IRequest<ObtenerOrdenCompraOrigenResponse?>;

public sealed record ObtenerOrdenCompraOrigenResponse(
    Guid OrdenCompraOrigenId,
    string FolioOrigen);
