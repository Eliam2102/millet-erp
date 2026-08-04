using Millet.Compras.Domain.Oc.Events;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Resultado de <see cref="OrdenCompra.Cancelar"/> (F4-PR3). Devuelve
/// el evento principal + la lista distinta de RequisicionIds que
/// tenían líneas en la OC y deben liberarse via
/// <see cref="Domain.Requisicion.LiberarDeOc"/>.
///
/// La lista incluye todas las RQs únicas (no líneas duplicadas) — el
/// handler invoca el listener una vez por RQ.
/// </summary>
public sealed record CancelarResultado(
    OrdenCompraCanceladaEvent EventoCancelada,
    IReadOnlyList<Guid> RequisicionesALiberar);
