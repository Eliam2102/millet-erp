using Millet.SharedKernel.Application.Integration;

namespace Millet.Compras.Application.Integration;

/// <summary>
/// Integration event: la requisición fue rechazada por un autorizador
/// (estado terminal). EventType <c>compras.requisicion.rechazada.v1</c>.
/// </summary>
public sealed record RequisicionRechazadaIntegrationEvent(
    Guid EmpresaId,
    DateTimeOffset OcurridoEn,
    Guid RequisicionId,
    Guid MotivoId,
    string? MotivoTexto,
    Guid ActorId)
    : IntegrationEvent("compras.requisicion.rechazada.v1", EmpresaId, OcurridoEn);
