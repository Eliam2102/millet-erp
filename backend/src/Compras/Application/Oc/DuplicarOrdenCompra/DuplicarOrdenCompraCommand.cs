using MediatR;

namespace Millet.Compras.Application.Oc.DuplicarOrdenCompra;

/// <summary>Duplica cantidades no recibidas. Las líneas heredadas conservan su RQ;
/// una OC rechazada con RQ conserva su compromiso y debe corregirse en la misma OC.</summary>
public sealed record DuplicarOrdenCompraCommand(
    Guid OrdenCompraOrigenId,
    string SucursalCodigo,
    short FolioAnio,
    DateOnly FechaDocumento) : IRequest<DuplicarOrdenCompraResponse>;

public sealed record DuplicarOrdenCompraResponse(
    Guid OrdenCompraNuevaId,
    string FolioNuevo,
    Guid OrdenCompraOrigenId,
    string FolioOrigen);
