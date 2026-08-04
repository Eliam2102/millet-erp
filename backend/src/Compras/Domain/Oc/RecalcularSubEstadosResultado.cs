using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de un <c>RegistrarRecepcion / Facturacion / Pago / Devolucion</c>
/// sobre la OC (F5-PR2): al menos uno de los dos eventos puede dispararse —
/// nunca los dos en la misma llamada.
/// </summary>
public sealed record RecalcularSubEstadosResultado(
    OrdenCompraCerradaEvent? Cerrada,
    OrdenCompraReabriertaEvent? Reabrierta);
