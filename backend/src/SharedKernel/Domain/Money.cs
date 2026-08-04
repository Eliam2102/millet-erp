using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.Domain;

/// <summary>
/// Value object inmutable que representa un monto en una moneda específica.
/// Las operaciones aritméticas validan que las monedas coincidan; las
/// conversiones requieren paso explícito de tipo de cambio.
/// Ver ADR-0014.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Mxn(decimal amount) => new(amount, "MXN");

    public static Money Usd(decimal amount) => new(amount, "USD");

    public static Money Of(decimal amount, string currency) => new(amount, currency);

    public Money Add(Money other) =>
        Currency != other.Currency
            ? throw new IncompatibleCurrenciesException(Currency, other.Currency)
            : new Money(Amount + other.Amount, Currency);

    public Money Subtract(Money other) =>
        Currency != other.Currency
            ? throw new IncompatibleCurrenciesException(Currency, other.Currency)
            : new Money(Amount - other.Amount, Currency);

    public Money Multiply(decimal factor) => new(Amount * factor, Currency);

    public Money Round(int decimals = 2, MidpointRounding mode = MidpointRounding.ToEven) =>
        new(Math.Round(Amount, decimals, mode), Currency);

    public Money ConvertTo(string targetCurrency, decimal exchangeRate) =>
        new(Amount * exchangeRate, targetCurrency);
}
