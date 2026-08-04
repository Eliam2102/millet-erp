using Microsoft.AspNetCore.Http;
using Millet.SharedKernel.Infrastructure;

namespace Millet.SharedKernel.UnitTests.Application;

public class CurrentEmpresaContextTests
{
    /// <summary>
    /// Helper: HttpContextAccessor sin HttpContext asignado simula la situación
    /// "fuera de un request HTTP" (jobs, tests, migrations). En esa condición
    /// Current debe ser null y Bypass debe seguir funcionando.
    /// </summary>
    private static CurrentEmpresaContext CreateOutsideRequest()
        => new(new HttpContextAccessor());

    [Fact]
    public void Should_NotBeBypassed_When_Default()
    {
        var context = CreateOutsideRequest();

        context.IsBypassed.Should().BeFalse();
    }

    [Fact]
    public void Should_BeBypassed_When_InsideBypassScope()
    {
        var context = CreateOutsideRequest();

        using (context.Bypass())
        {
            context.IsBypassed.Should().BeTrue();
        }

        context.IsBypassed.Should().BeFalse();
    }

    [Fact]
    public void Should_PreserveOuterState_When_NestedBypass()
    {
        var context = CreateOutsideRequest();

        using (context.Bypass())
        {
            using (context.Bypass())
            {
                context.IsBypassed.Should().BeTrue();
            }
            // El scope interno se cerró, pero el externo sigue activo.
            context.IsBypassed.Should().BeTrue();
        }

        context.IsBypassed.Should().BeFalse();
    }

    [Fact]
    public async Task Should_IsolateBypass_AcrossAsyncFlows()
    {
        var context = CreateOutsideRequest();
        context.IsBypassed.Should().BeFalse();

        var taskInsideBypass = Task.Run(async () =>
        {
            using (context.Bypass())
            {
                await Task.Delay(50);
                return context.IsBypassed;
            }
        });

        var taskOutsideBypass = Task.Run(async () =>
        {
            await Task.Delay(20);
            return context.IsBypassed;
        });

        var (inside, outside) = (await taskInsideBypass, await taskOutsideBypass);

        inside.Should().BeTrue();
        outside.Should().BeFalse();
    }

    [Fact]
    public void Should_ReturnNullCurrent_When_NoHttpContext()
    {
        // Sin HttpContext asignado en el accessor (escenario fuera de request:
        // jobs, migrations, arranque), Current devuelve null y los interceptors
        // EF Core toleran ese caso vía bypass o vía null fallback.
        var context = CreateOutsideRequest();

        context.Current.Should().BeNull();
    }
}
