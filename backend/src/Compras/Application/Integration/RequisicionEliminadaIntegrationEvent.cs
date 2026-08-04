using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: la requisición fue eliminada pre-autorización
/// (estado terminal). EventType <c>compras.requisicion.eliminada.v1</c>.
/// </summary>
public sealed record RequisicionEliminadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId)
    : IntegrationEvent("compras.requisicion.eliminada.v1", EmpresaId, OcurridoEn);
