namespace Millet.Compras.Application.Umbrales;

/// <summary>
/// Umbrales vigentes de un departamento. <c>UmbralMenorCuantia</c> es N1
/// (<c>null</c> = sin umbral N1); <c>UmbralMonto</c> es N2.
/// </summary>
public sealed record UmbralAprobacionResponse(
    Guid DepartamentoId,
    decimal? UmbralMenorCuantia,
    decimal UmbralMonto,
    string Moneda,
    DateOnly VigenteDesde);
