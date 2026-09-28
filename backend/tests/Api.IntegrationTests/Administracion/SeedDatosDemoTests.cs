using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Seed;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// F2 (Parte F, base de desarrollo limpia): la fase de datos demo de
/// <see cref="CatalogosTestSeedHostedService"/> solo corre con
/// <c>Seed:DatosDemo:Habilitado=true</c> — el resto de la suite lo fuerza
/// a <c>false</c> vía <c>TestAssemblyInit</c>. Este test levanta un host
/// aparte con el flag en <c>true</c> (misma BD aislada del gate, vía
/// <see cref="WebApplicationFactory{TEntryPoint}.WithWebHostBuilder"/>) y
/// comprueba los conteos del seed demo, y que un segundo arranque no
/// duplica nada (idempotencia por clave de negocio).
/// </summary>
public class SeedDatosDemoTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public SeedDatosDemoTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
    }

    [Fact]
    public async Task DatosDemo_Habilitado_Siembra_Los_Conteos_Esperados_Y_El_Segundo_Arranque_No_Duplica()
    {
        var (deptos1, puestos1, asignaciones1, empleados1, inactivos1) = await ArrancarYContarAsync();
        var (deptos2, puestos2, asignaciones2, empleados2, inactivos2) = await ArrancarYContarAsync();

        // Conteos del primer arranque: exactamente el contenido del seed.
        Assert.Equal(CatalogosTestSeedHostedService.DemoDepartamentos.Length, deptos1);
        Assert.Equal(CatalogosTestSeedHostedService.DemoPuestos.Length, puestos1);
        Assert.Equal(CatalogosTestSeedHostedService.DemoAsignacionesPuesto.Length, asignaciones1);
        Assert.Equal(CatalogosTestSeedHostedService.DemoEmpleados.Length, empleados1);
        Assert.Equal(2, inactivos1);

        // Idempotencia: segundo arranque contra la misma BD no duplica.
        Assert.Equal(deptos1, deptos2);
        Assert.Equal(puestos1, puestos2);
        Assert.Equal(asignaciones1, asignaciones2);
        Assert.Equal(empleados1, empleados2);
        Assert.Equal(inactivos1, inactivos2);
    }

    private async Task<(int Deptos, int Puestos, int Asignaciones, int Empleados, int Inactivos)> ArrancarYContarAsync()
    {
        await using var factory = _base.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Seed:DatosDemo:Habilitado"] = "true",
                })));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

        var deptoClaves = CatalogosTestSeedHostedService.DemoDepartamentos.Select(d => d.Clave).ToArray();
        var puestoClaves = CatalogosTestSeedHostedService.DemoPuestos.Select(p => p.Clave).ToArray();
        var asignacionIds = CatalogosTestSeedHostedService.DemoAsignacionesPuesto.Select(a => a.Id).ToArray();
        var empleadoClaves = CatalogosTestSeedHostedService.DemoEmpleados.Select(e => e.Clave).ToArray();

        var deptos = await db.Departamentos.CountAsync(d => deptoClaves.Contains(d.Clave));
        var puestos = await db.Puestos.CountAsync(p => puestoClaves.Contains(p.Clave));
        var asignaciones = await db.SucursalPuestos.CountAsync(a => asignacionIds.Contains(a.Id));
        var empleados = await db.Empleados.CountAsync(e => empleadoClaves.Contains(e.Clave));
        var inactivos = await db.Empleados.CountAsync(
            e => empleadoClaves.Contains(e.Clave) && e.Estatus == EstatusCatalogo.Inactivo);

        return (deptos, puestos, asignaciones, empleados, inactivos);
    }
}
