using MediatR;

namespace Millet.Compras.Application.Oc.Lineas.EliminarLinea;

/// <summary>
/// Elimina una línea de OC. Solo permitido en <c>Borrador</c>/<c>Rechazada</c>
/// y si la línea no tiene recepción ni facturación registrada.
/// </summary>
public sealed record EliminarLineaOcCommand(
    Guid OrdenCompraId,
    Guid LineaId) : IRequest;
