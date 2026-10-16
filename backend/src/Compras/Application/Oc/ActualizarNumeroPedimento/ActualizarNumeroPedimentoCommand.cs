using MediatR;

namespace Millet.Compras.Application.Oc.ActualizarNumeroPedimento;

/// <summary>
/// Actualiza los datos de importación capturados post-autorización (§4.7):
/// <c>NumeroPedimento</c> y, junto a él, <c>NumeroBl</c> (BL o AWB),
/// <c>FechaEstimadaArribo</c> y <c>AgenteAduanalNaviera</c>. Reemplaza los
/// cuatro campos: null limpia. PLATFORM-TODO(F1-COM-06): supuesto Vilo.
/// </summary>
public sealed record ActualizarNumeroPedimentoCommand(
    Guid OrdenCompraId,
    string? NumeroPedimento,
    string? NumeroBl = null,
    DateOnly? FechaEstimadaArribo = null,
    string? AgenteAduanalNaviera = null) : IRequest;
