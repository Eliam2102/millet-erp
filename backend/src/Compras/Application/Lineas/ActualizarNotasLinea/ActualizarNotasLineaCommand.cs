using MediatR;

namespace Millet.Compras.Application.Lineas.ActualizarNotasLinea;

/// <summary>
/// Comando para actualizar solo las notas de una línea. Permitido en
/// cualquier estado **no terminal** de la requisición (§4.2). Útil para
/// anotaciones operativas: comentarios del autorizador, observaciones
/// del comprador durante recepción, etc.
/// </summary>
public sealed record ActualizarNotasLineaCommand(
    Guid RequisicionId,
    Guid LineaId,
    string? Notas) : IRequest<Unit>;
