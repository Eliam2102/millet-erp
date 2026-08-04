using MediatR;

namespace Millet.Compras.Application.Oc.ActualizarNumeroPedimento;

/// <summary>
/// Actualiza solo el <c>NumeroPedimento</c> de la información de
/// importación (§4.7). Editable post-autorización — se captura cuando
/// llega el material.
/// </summary>
public sealed record ActualizarNumeroPedimentoCommand(
    Guid OrdenCompraId,
    string? NumeroPedimento) : IRequest;
