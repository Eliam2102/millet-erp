using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Exceptions;

namespace Millet.SharedKernel.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Should_CreateMoney_When_UsingOf()
    {
        var m = Money.Of(100m, "MXN");

        m.Amount.Should().Be(100m);
        m.Currency.Should().Be("MXN");
    }

    [Fact]
    public void Should_CreateMxn_When_UsingMxnFactory()
    {
        var m = Money.Mxn(50m);

        m.Currency.Should().Be("MXN");
        m.Amount.Should().Be(50m);
    }

    [Fact]
    public void Should_CreateUsd_When_UsingUsdFactory()
    {
        var m = Money.Usd(20m);

        m.Currency.Should().Be("USD");
        m.Amount.Should().Be(20m);
    }

    [Fact]
    public void Should_SumAmounts_When_AddingSameCurrency()
    {
        var sum = Money.Mxn(100m).Add(Money.Mxn(50m));

        sum.Amount.Should().Be(150m);
        sum.Currency.Should().Be("MXN");
    }

    [Fact]
    public void Should_RejectIncompatibleCurrencies_When_AddingMxnToUsd()
    {
        var act = () => Money.Mxn(100m).Add(Money.Usd(50m));

        var ex = act.Should().Throw<IncompatibleCurrenciesException>().Which;
        ex.LeftCurrency.Should().Be("MXN");
        ex.RightCurrency.Should().Be("USD");
        ex.Code.Should().Be("INCOMPATIBLE_CURRENCIES");
    }

    [Fact]
    public void Should_SubtractAmounts_When_SubtractingSameCurrency()
    {
        var diff = Money.Mxn(100m).Subtract(Money.Mxn(30m));

        diff.Amount.Should().Be(70m);
        diff.Currency.Should().Be("MXN");
    }

    [Fact]
    public void Should_RejectIncompatibleCurrencies_When_SubtractingMxnFromUsd()
    {
        var act = () => Money.Mxn(100m).Subtract(Money.Usd(30m));

        act.Should().Throw<IncompatibleCurrenciesException>();
    }

    [Fact]
    public void Should_ScaleAmount_When_Multiplying()
    {
        var m = Money.Mxn(100m).Multiply(0.16m);

        m.Amount.Should().Be(16m);
        m.Currency.Should().Be("MXN");
    }

    [Fact]
    public void Should_ApplyBankerRounding_When_RoundingDefault()
    {
        // 12.345 redondeado a 2 decimales con banker's rounding (ToEven) → 12.34
        var m = Money.Of(12.345m, "MXN").Round();

        m.Amount.Should().Be(12.34m);
    }

    [Fact]
    public void Should_RoundAwayFromZero_When_SpecifyingMode()
    {
        var m = Money.Of(12.345m, "MXN").Round(2, MidpointRounding.AwayFromZero);

        m.Amount.Should().Be(12.35m);
    }

    [Fact]
    public void Should_ConvertAmount_When_UsingConvertTo()
    {
        var usd = Money.Mxn(1000m).ConvertTo("USD", 0.05m);

        usd.Amount.Should().Be(50m);
        usd.Currency.Should().Be("USD");
    }

    [Fact]
    public void Should_NotMutateOriginal_When_PerformingOperation()
    {
        var original = Money.Mxn(100m);
        var modified = original.Add(Money.Mxn(50m));

        original.Amount.Should().Be(100m);
        modified.Amount.Should().Be(150m);
        original.Should().NotBe(modified);
    }

    [Fact]
    public void Should_BeEqual_When_SameAmountAndCurrency()
    {
        var a = Money.Mxn(100m);
        var b = Money.Mxn(100m);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Should_NotBeEqual_When_DifferentCurrency()
    {
        Money.Of(100m, "MXN").Should().NotBe(Money.Of(100m, "USD"));
    }
}
