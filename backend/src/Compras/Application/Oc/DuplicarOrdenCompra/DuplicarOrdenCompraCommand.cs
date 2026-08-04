using MediatR;

namespace Millet.Compras.Application.Oc.DuplicarOrdenCompra;

/// <summary>
/// Duplica una OC Cancelada o Rechazada como una OC nueva en
/// <see cref="Domain.Oc.EstadoOrdenCompra.Borrador"/> (F6-PR2, C4 del
/// diseño). Hereda cabecera (proveedor, sucursal, almacén, condiciones,
/// uso, moneda, banderas, observaciones, logística, importación) y
/// líneas como **manuales** (sin FK a RQ — el comprador re-selecciona).
///
/// <para>
/// NO copia: adjuntos, autorizaciones, sub-estados, motivos de
/// rechazo/cancelación, vínculos a RQs originales.
/// </para>
/// </summary>
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
