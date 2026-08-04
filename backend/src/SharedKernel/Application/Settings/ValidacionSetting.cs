namespace Millet.SharedKernel.Application.Settings;

/// <summary>
/// Validaciones declarativas que el endpoint genérico de PATCH aplica
/// al valor entrante antes de invocar al provider. Si la validación
/// falla, el endpoint retorna 422 con <c>ValidationProblemDetails</c>.
///
/// <list type="bullet">
///   <item><see cref="Min"/> / <see cref="Max"/> — solo aplican a
///         <see cref="TipoSetting.Int"/> y <see cref="TipoSetting.Decimal"/>.</item>
///   <item><see cref="Pattern"/> — solo aplica a
///         <see cref="TipoSetting.String"/>. Regex en sintaxis .NET.</item>
///   <item><see cref="Opciones"/> — solo aplica a
///         <see cref="TipoSetting.Enum"/>. Si está poblado, el valor
///         debe ser exactamente una de las opciones (case-sensitive).</item>
/// </list>
/// Ver ADR-0034.
/// </summary>
public sealed record ValidacionSetting(
    decimal? Min = null,
    decimal? Max = null,
    string? Pattern = null,
    IReadOnlyList<string>? Opciones = null);
