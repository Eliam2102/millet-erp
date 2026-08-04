using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1: OC transmitida del Borrador a EnAutorización
/// Jefe Compras (§5.3 del diseño). Consumers: notificaciones, BI.
/// EventType: <c>compras.orden-compra.enviada-a-autorizacion.v1</c>.
/// </summary>
public sealed record OcEnviadaAAutorizacionIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId)
    : IntegrationEvent("compras.orden-compra.enviada-a-autorizacion.v1", EmpresaId, OcurridoEn);
