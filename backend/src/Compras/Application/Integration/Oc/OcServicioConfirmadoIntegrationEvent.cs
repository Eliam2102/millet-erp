using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration.Oc;

/// <summary>
/// Integration event v1 (86e3tacbn): el área solicitante confirmó (<c>Confirmado = true</c>) o deshizo la
/// conformidad del servicio de la OC. CxP lo usa para republicar el pasivo con la elegibilidad nueva: las líneas de
/// servicio solo cuentan como recibidas con la OC confirmada. EventType: <c>compras.orden-compra.servicio-confirmado.v1</c>.
/// </summary>
public sealed record OcServicioConfirmadoIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid OrdenCompraId,
    string Folio,
    bool Confirmado,
    Guid UsuarioId,
    string? Texto)
    : IntegrationEvent("compras.orden-compra.servicio-confirmado.v1", EmpresaId, OcurridoEn);
