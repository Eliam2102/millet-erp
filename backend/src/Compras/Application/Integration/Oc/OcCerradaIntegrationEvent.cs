using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1: OC cerrada automáticamente (las 3 dimensiones
/// — recepción, facturación, pago — cumplen sus criterios de cierre).
/// EventType: <c>compras.orden-compra.cerrada.v1</c>.
/// </summary>
public sealed record OcCerradaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId)
    : IntegrationEvent("compras.orden-compra.cerrada.v1", EmpresaId, OcurridoEn);
