using Millet.SharedKernel.Application;

namespace Millet.SharedKernel.UnitTests.Application;

public class AuthModeValidatorTests
{
    [Fact]
    public void Should_NotThrow_When_EntraIdInDevelopment()
    {
        var act = () => AuthModeValidator.EnsureAllowedForEnvironment(AuthMode.EntraId, isDevelopment: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Should_NotThrow_When_EntraIdInProduction()
    {
        var act = () => AuthModeValidator.EnsureAllowedForEnvironment(AuthMode.EntraId, isDevelopment: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void Should_NotThrow_When_FakeForLocalDevInDevelopment()
    {
        var act = () => AuthModeValidator.EnsureAllowedForEnvironment(AuthMode.FakeForLocalDev, isDevelopment: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void Should_Throw_When_FakeForLocalDevOutsideDevelopment()
    {
        var act = () => AuthModeValidator.EnsureAllowedForEnvironment(AuthMode.FakeForLocalDev, isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*FakeForLocalDev*Development*");
    }
}
