using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain.Oc;

/// <summary>
/// Value object inmutable que representa el folio de una orden de compra.
/// Formato canónico (§4.3 del diseño): <c>OC-{prefijoSucursal}{año}-{secuencial:6}</c>,
/// ej. <c>OC-MID2026-000001</c>.
///
/// El prefijo <c>OC-</c> es fijo y diferencia las OCs de las requisiciones
/// (que usan el patrón sin prefijo). El prefijo de sucursal es 2-4 letras
/// mayúsculas, el año 4 dígitos y el secuencial 6 dígitos con padding de
/// ceros. La unicidad por <c>(empresa_id, sucursal_destino_id, folio)</c>
/// está garantizada a nivel BD (UNIQUE en §10.1) y la generación atómica
/// vía <c>compras.folio_secuencias_oc</c> (F1-PR2).
/// </summary>
public sealed partial record Folio
{
    private const string Pattern = @"^OC-[A-Z]{2,4}\d{4}-\d{6}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex FolioRegex();

    public string Valor { get; }

    private Folio(string valor)
    {
        Valor = valor;
    }

    /// <summary>
    /// Construye un <see cref="Folio"/> validando el formato. Lanza
    /// <see cref="BusinessRuleException"/> con código
    /// <c>FOLIO_OC_FORMATO_INVALIDO</c> si no matchea el patrón.
    /// </summary>
    public static Folio Parse(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new BusinessRuleException(
                "FOLIO_OC_FORMATO_INVALIDO",
                "El folio de la OC no puede ser vacío.");
        }

        if (!FolioRegex().IsMatch(valor))
        {
            throw new BusinessRuleException(
                "FOLIO_OC_FORMATO_INVALIDO",
                $"El folio '{valor}' no cumple el formato 'OC-{{prefijoSucursal}}{{anio}}-{{secuencial:6}}' (ej. OC-MID2026-000001).");
        }

        return new Folio(valor);
    }

    public override string ToString() => Valor;
}
