using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: la requisición cumplió la matriz de aprobación
/// y transicionó a <c>Autorizada</c> (incluso si la bifurcación
/// stock-aware luego la lleva a <c>Cerrada</c> o <c>EnSurtido</c>; el
/// momento de aprobación es lo que se reporta).
///
/// <para>
/// EventType: <c>compras.requisicion.autorizada.v1</c>. Shape mínimo
/// (RequisicionId + audit). Consumers que necesiten Folio, líneas,
/// etc. consultan Compras vía API.
/// </para>
/// </summary>
public sealed record RequisicionAutorizadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId)
    : IntegrationEvent("compras.requisicion.autorizada.v1", EmpresaId, OcurridoEn);
