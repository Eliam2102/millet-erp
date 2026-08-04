using MediatR;

namespace Millet.Compras.Application.Lineas.EliminarLinea;

/// <summary>
/// Comando para eliminar una línea de una requisición. Solo permitido
/// en estado <c>Borrador</c> (las líneas en estados posteriores son
/// auditables y no se eliminan).
/// </summary>
public sealed record EliminarLineaCommand(
    Guid RequisicionId,
    Guid LineaId) : IRequest<Unit>;
