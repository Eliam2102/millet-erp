using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: la requisición fue cancelada post-autorización
/// (libera reservas + aborta OC borrador). EventType
/// <c>compras.requisicion.cancelada.v1</c>.
/// </summary>
public sealed record RequisicionCanceladaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId)
    : IntegrationEvent("compras.requisicion.cancelada.v1", EmpresaId, OcurridoEn);
