using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1: OC rechazada en flujo de autorización (N1 o N2).
/// Lleva motivo (id + texto) para que consumers de notificaciones
/// puedan rendar email al comprador. EventType:
/// <c>compras.orden-compra.rechazada.v1</c>.
/// </summary>
public sealed record OcRechazadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    Guid CompradorTitularId,
    Guid UsuarioRechazadorId,
    Guid MotivoRechazoId,
    string? MotivoRechazoTexto)
    : IntegrationEvent("compras.orden-compra.rechazada.v1", EmpresaId, OcurridoEn);
