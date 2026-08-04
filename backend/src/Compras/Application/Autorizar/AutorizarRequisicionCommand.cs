using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Autorizar;

/// <summary>
/// Comando para registrar una autorización (Nivel1 o Nivel2) sobre una
/// requisición en estado <c>EnAutorizacion</c>. El validador del permiso
/// específico (<c>autorizar-nivel1</c> / <c>autorizar-nivel2</c>) vive
/// en el endpoint Api, no en el handler.
/// </summary>
public sealed record AutorizarRequisicionCommand(
    Guid RequisicionId,
    NivelAutorizacion Nivel,
    string? Notas = null) : IRequest<Unit>;
