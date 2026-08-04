using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1: OC cancelada (cualquier path — sin recepciones
/// o con recepciones parciales). Los consumers que necesiten detalle
/// adicional (e.g. detectar liberación parcial de RQs) consultan vía
/// API. EventType: <c>compras.orden-compra.cancelada.v1</c>.
/// </summary>
public sealed record OcCanceladaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId,
    Guid UsuarioCanceladorId,
    Guid MotivoCancelacionId,
    string? MotivoCancelacionTexto)
    : IntegrationEvent("compras.orden-compra.cancelada.v1", EmpresaId, OcurridoEn);
