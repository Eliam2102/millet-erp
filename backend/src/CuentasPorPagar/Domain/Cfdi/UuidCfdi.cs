using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Value object inmutable que representa el UUID fiscal de un CFDI 4.0
/// (Folio Fiscal del SAT). Formato: GUID v4 con guiones, en mayúsculas
/// canónicas como emite el SAT (ej. <c>5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B</c>).
///
/// <para>
/// La unicidad se garantiza a nivel BD vía índice único sobre
/// <c>cuentas_por_pagar.cfdis_recibidos.uuid_cfdi</c>. El VO normaliza a
/// mayúsculas en <see cref="Parse"/> para que la comparación sea
/// case-insensitive aunque el origen del XML use minúsculas.
/// </para>
/// </summary>
public sealed partial record UuidCfdi
{
    private const string Pattern = @"^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex UuidRegex();

    public string Valor { get; }

    private UuidCfdi(string valor)
    {
        Valor = valor;
    }

    public static UuidCfdi Parse(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new BusinessRuleException(
                "UUID_CFDI_VACIO",
                "El UUID del CFDI no puede ser vacío.");
        }

        var normalizado = valor.Trim().ToUpperInvariant();
        if (!UuidRegex().IsMatch(normalizado))
        {
            throw new BusinessRuleException(
                "UUID_CFDI_FORMATO_INVALIDO",
                $"El UUID '{valor}' no cumple el formato GUID del SAT (8-4-4-4-12 hex).");
        }

        return new UuidCfdi(normalizado);
    }

    public override string ToString() => Valor;
}
