using System.Text.RegularExpressions;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Value Object: <c>**** **** **** 1234</c> — solo últimos 4 dígitos
/// (§3.2 del anexo TC). Nunca se almacena el PAN completo: aunque el
/// ERP no procesa pagos, no almacenar PAN reduce superficie de riesgo
/// PCI-DSS-ish.
/// </summary>
public sealed partial record NumeroTarjetaEnmascarado
{
    private const string Pattern = @"^\*{4} \*{4} \*{4} \d{4}$";

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex EnmascaradoRegex();

    public string Valor { get; }
    public string UltimosCuatro { get; }

    private NumeroTarjetaEnmascarado(string valor, string ultimosCuatro)
    {
        Valor = valor;
        UltimosCuatro = ultimosCuatro;
    }

    public static NumeroTarjetaEnmascarado FromUltimosCuatro(string ultimosCuatro)
    {
        if (string.IsNullOrWhiteSpace(ultimosCuatro) || ultimosCuatro.Length != 4 || !ultimosCuatro.All(char.IsDigit))
        {
            throw new BusinessRuleException(
                "TC_ULTIMOS_CUATRO_INVALIDO",
                $"Los últimos 4 dígitos deben ser 4 caracteres numéricos (recibido: '{ultimosCuatro}').");
        }
        return new NumeroTarjetaEnmascarado($"**** **** **** {ultimosCuatro}", ultimosCuatro);
    }

    public static NumeroTarjetaEnmascarado Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new BusinessRuleException("TC_ENMASCARADO_VACIO",
                "El número enmascarado no puede ser vacío.");
        if (!EnmascaradoRegex().IsMatch(raw))
            throw new BusinessRuleException(
                "TC_ENMASCARADO_FORMATO_INVALIDO",
                $"El número '{raw}' no cumple el formato '**** **** **** NNNN'.");

        var ultimos = raw[^4..];
        return new NumeroTarjetaEnmascarado(raw, ultimos);
    }

    public override string ToString() => Valor;
}
