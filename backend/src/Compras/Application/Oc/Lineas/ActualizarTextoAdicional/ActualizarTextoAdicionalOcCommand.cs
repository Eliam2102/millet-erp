using MediatR;

namespace Millet.Compras.Application.Oc.Lineas.ActualizarTextoAdicional;

/// <summary>
/// Actualiza solo el campo <c>texto_adicional</c> de una línea de OC.
/// Permitido en cualquier estado **no terminal**, incluso post-recepción
/// (§4.2 — el texto sigue editable aunque los campos estructurales se
/// bloqueen).
/// </summary>
public sealed record ActualizarTextoAdicionalOcCommand(
    Guid OrdenCompraId,
    Guid LineaId,
    string? TextoAdicional) : IRequest;
