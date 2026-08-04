using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Domain.NotaCargo;

/// <summary>
/// Folio interno de Nota de Cargo (§A3 del 01-diseno). Formato canónico
/// <c>NCG-{año}-{secuencial:6}</c> (ej. <c>NCG-2026-000001</c>).
///
/// <para>
/// Generación atómica vía secuencia PostgreSQL por año
/// (<c>folio_secuencias_nota_cargo</c>). El handler de
/// <c>CrearNotaCargoCommand</c> obtiene el siguiente secuencial con
/// <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> antes de
/// invocar este constructor. Unicidad por año (no rollover).
/// </para>
/// </summary>
public sealed partial record FolioInternoNotaCargo
{
    private const string Pattern = @"^NCG-\d{4}-\d{6}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex FolioRegex();

    public string Valor { get; }
    public int Anio { get; }
    public int Secuencial { get; }

    private FolioInternoNotaCargo(string valor, int anio, int secuencial)
    {
        Valor = valor;
        Anio = anio;
        Secuencial = secuencial;
    }

    public static FolioInternoNotaCargo FromAnioSecuencial(int anio, int secuencial)
    {
        if (anio < 2020 || anio > 2099)
            throw new BusinessRuleException(
                "FOLIO_NCG_ANIO_INVALIDO",
                $"El año del folio NCG debe estar entre 2020 y 2099 (recibido: {anio}).");
        if (secuencial < 1 || secuencial > 999_999)
            throw new BusinessRuleException(
                "FOLIO_NCG_SECUENCIAL_INVALIDO",
                $"El secuencial del folio NCG debe estar entre 1 y 999_999 (recibido: {secuencial}).");

        var valor = $"NCG-{anio:D4}-{secuencial:D6}";
        return new FolioInternoNotaCargo(valor, anio, secuencial);
    }

    public static FolioInternoNotaCargo Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new BusinessRuleException("FOLIO_NCG_VACIO", "El folio NCG no puede ser vacío.");
        if (!FolioRegex().IsMatch(raw))
            throw new BusinessRuleException(
                "FOLIO_NCG_FORMATO_INVALIDO",
                $"El folio '{raw}' no cumple el formato NCG-{{año:4}}-{{secuencial:6}}.");

        var partes = raw.Split('-');
        return new FolioInternoNotaCargo(raw, int.Parse(partes[1]), int.Parse(partes[2]));
    }

    public override string ToString() => Valor;
}
