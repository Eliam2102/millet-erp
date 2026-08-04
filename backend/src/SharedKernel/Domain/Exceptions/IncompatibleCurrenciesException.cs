namespace Millet.SharedKernel.Domain.Exceptions;

/// <summary>
/// Lanzada cuando se intenta operar aritméticamente sobre dos
/// <see cref="Money"/> con monedas distintas. Indica un error de
/// programación: las conversiones deben ser explícitas vía
/// <c>Money.ConvertTo</c>. Mapea a HTTP 422.
/// </summary>
public sealed class IncompatibleCurrenciesException : DomainException
{
    public override string Code => "INCOMPATIBLE_CURRENCIES";

    public string LeftCurrency { get; }

    public string RightCurrency { get; }

    public IncompatibleCurrenciesException(string left, string right)
        : base($"Las monedas '{left}' y '{right}' son incompatibles. La conversión debe ser explícita vía Money.ConvertTo.")
    {
        LeftCurrency = left;
        RightCurrency = right;
    }
}
