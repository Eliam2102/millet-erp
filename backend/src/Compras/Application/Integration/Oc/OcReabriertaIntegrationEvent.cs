using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1: OC reabierta tras estar <c>Cerrada</c> (una
/// devolución o nota de crédito dejó alguna dimensión fuera de cierre).
/// EventType: <c>compras.orden-compra.reabierta.v1</c>.
/// </summary>
public sealed record OcReabriertaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId)
    : IntegrationEvent("compras.orden-compra.reabierta.v1", EmpresaId, OcurridoEn);
