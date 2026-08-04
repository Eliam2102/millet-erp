using MediatR;

namespace Millet.Compras.Application.EnviarAAutorizacion;

/// <summary>
/// Comando para transmitir una requisición a autorización (Borrador → EnAutorizacion).
/// </summary>
public sealed record EnviarAAutorizacionCommand(Guid RequisicionId) : IRequest<Unit>;
