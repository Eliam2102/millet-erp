using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1: OC autorizada en nivel 2 (Dirección) → transición
/// a estado <c>Autorizada</c>. Marca el inicio del ciclo de recepción /
/// facturación / pago. EventType:
/// <c>compras.orden-compra.autorizada.v1</c>.
/// </summary>
public sealed record OcAutorizadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId,
    DateTimeOffset FechaContabilizacion)
    : IntegrationEvent("compras.orden-compra.autorizada.v1", EmpresaId, OcurridoEn);
