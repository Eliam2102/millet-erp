using System.Text.RegularExpressions;

namespace Millet.SharedKernel.Domain;

/// <summary>
/// M-158 (86e3zr6pg): en el Complemento de Comercio Exterior 2.0 la fracción arancelaria de la
/// mercancía exportada lleva <b>10 dígitos</b> (fracción de 8 + número de identificación comercial,
/// NICO, de 2); con 8 el PAC rechaza el timbre. Regla única para Comercio Exterior (revisión de la
/// partida embarcada) y Facturación (emisión). El maestro de producto A+W sigue aceptando 8 a 10
/// porque también lo usan Compras e importación.
/// </summary>
public static partial class FraccionArancelariaExportacion
{
    public const string Codigo = "CCE_FRACCION_INVALIDA";
    public const string Mensaje = "La fracción debe tener 10 dígitos (fracción arancelaria + NICO).";

    public static bool EsValida(string? fraccion) =>
        fraccion is not null && Formato().IsMatch(fraccion.Trim());

    [GeneratedRegex(@"^\d{10}$")]
    private static partial Regex Formato();
}
