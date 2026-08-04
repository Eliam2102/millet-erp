using Millet.Compras.Domain;

namespace Millet.Compras.Application.ObtenerRequisicionPorId;

/// <summary>
/// DTO de una autorización dentro de <see cref="RequisicionResponse"/>
/// (B.0). Incluye vigentes y cerradas — el agregado solo guarda firmas
/// reales, no estados intermedios.
/// </summary>
public sealed record AutorizacionResponse(
    Guid Id,
    NivelAutorizacion Nivel,
    Guid UsuarioId,
    DateTimeOffset FechaHora,
    string? Notas);
