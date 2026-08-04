using MediatR;
using Millet.Compras.Application.Oc.ListarOrdenesCompra;

namespace Millet.Compras.Application.Oc.ListarHermanasDuplicadas;

/// <summary>
/// OCs hermanas duplicadas (F7-PR3, brecha §14.2 del 05-frontend).
/// Dado un <c>ocOrigenId</c>, devuelve todas las OCs cuya
/// <c>oc_origen_id</c> apunta a él.
/// </summary>
public sealed record ListarHermanasDuplicadasQuery(
    Guid OrdenCompraOrigenId) : IRequest<ListarHermanasDuplicadasResponse>;

public sealed record ListarHermanasDuplicadasResponse(
    IReadOnlyList<OrdenCompraResumen> Hermanas);
