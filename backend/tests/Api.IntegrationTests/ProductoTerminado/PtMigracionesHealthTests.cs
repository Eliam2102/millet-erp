using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Infrastructure.HealthChecks;

namespace Millet.Api.IntegrationTests.ProductoTerminado;

[Collection(ApiCompartida.Nombre)]
public class PtMigracionesHealthTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public void M56_El_health_check_de_migraciones_incluye_ProductoTerminadoDbContext()
    {
        var opciones = factory.Services.GetRequiredService<MigrationsHealthCheckOptions>();
        Assert.Contains(typeof(ProductoTerminadoDbContext), opciones.ContextTypes);
    }
}
