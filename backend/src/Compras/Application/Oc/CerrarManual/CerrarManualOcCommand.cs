using MediatR;

namespace Millet.Compras.Application.Oc.CerrarManual;

/// <summary>
/// Cierre manual de la OC (GAP-9). Válvula de escape para OCs que no
/// alcanzan el cierre automático — típicamente OCs de servicios (que no
/// registran recepción en Almacén) o con residuales que el proveedor
/// nunca surtirá/facturará. Requiere el permiso
/// <c>compras.ordenes.cerrar-manual</c>; solo válido desde
/// <c>Autorizada</c>.
/// </summary>
public sealed record CerrarManualOcCommand(Guid OrdenCompraId) : IRequest;
