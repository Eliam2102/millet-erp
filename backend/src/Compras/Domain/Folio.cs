using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain;

/// <summary>
/// Value object inmutable que representa el folio de una requisición.
/// Formato canónico (§4.5): <c>{prefijoSucursal}{año}-{secuencial:6}</c>,
/// ej. <c>MID2026-000001</c>.
///
/// El prefijo es 2-4 letras mayúsculas (código corto de la sucursal), el
/// año son 4 dígitos, y el secuencial son 6 dígitos con padding de ceros.
/// La unicidad por <c>(empresa_id, folio_anio, folio)</c> está garantizada
/// a nivel BD (UNIQUE en §10.1) y la generación es atómica vía
/// <c>compras.folio_secuencias</c> (F1-PR2).
/// </summary>
public sealed partial record Folio
{
    private const string Pattern = @"^[A-Z]{2,4}\d{4}-\d{6}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex FolioRegex();

    public string Valor { get; }

    private Folio(string valor)
    {
        Valor = valor;
    }

    /// <summary>
    /// Construye un <see cref="Folio"/> validando el formato. Lanza
    /// <see cref="BusinessRuleException"/> con código <c>FOLIO_FORMATO_INVALIDO</c>
    /// si no matchea el patrón.
    /// </summary>
    public static Folio Parse(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new BusinessRuleException(
                "FOLIO_FORMATO_INVALIDO",
                "El folio no puede ser vacío.");
        }

        if (!FolioRegex().IsMatch(valor))
        {
            throw new BusinessRuleException(
                "FOLIO_FORMATO_INVALIDO",
                $"El folio '{valor}' no cumple el formato '{{prefijoSucursal}}{{anio}}-{{secuencial:6}}' (ej. MID2026-000001).");
        }

        return new Folio(valor);
    }

    public override string ToString() => Valor;
}
