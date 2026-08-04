using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Value object inmutable que representa un RFC mexicano (Registro
/// Federal de Contribuyentes) validado estructuralmente (12 o 13
/// caracteres, formato SAT).
///
/// <para>
/// **Convención del módulo**: el RFC se almacena en mayúsculas. La
/// validación contra la LRFC vía FiscalAPI es responsabilidad del
/// flujo de alta de proveedor (no de este VO).
/// </para>
/// </summary>
public sealed partial record RfcMexicano
{
    private const string PatternMoral  = @"^[A-ZÑ&]{3}\d{6}[A-Z0-9]{3}$";
    private const string PatternFisica = @"^[A-ZÑ&]{4}\d{6}[A-Z0-9]{3}$";

    [GeneratedRegex(PatternMoral,  RegexOptions.CultureInvariant)] private static partial Regex RegexMoral();
    [GeneratedRegex(PatternFisica, RegexOptions.CultureInvariant)] private static partial Regex RegexFisica();

    public string Valor { get; }

    private RfcMexicano(string valor)
    {
        Valor = valor;
    }

    public static RfcMexicano Parse(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new BusinessRuleException(
                "RFC_VACIO",
                "El RFC no puede ser vacío.");
        }

        var normalizado = valor.Trim().ToUpperInvariant();
        if (!RegexMoral().IsMatch(normalizado) && !RegexFisica().IsMatch(normalizado))
        {
            throw new BusinessRuleException(
                "RFC_FORMATO_INVALIDO",
                $"El RFC '{valor}' no cumple el formato del SAT (12 caracteres persona moral, 13 persona física).");
        }

        return new RfcMexicano(normalizado);
    }

    public override string ToString() => Valor;
}
