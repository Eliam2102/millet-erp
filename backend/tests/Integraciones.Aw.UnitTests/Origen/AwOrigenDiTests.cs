using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;

namespace Millet.Integraciones.Aw.UnitTests.Origen;

public sealed class AwOrigenDiTests
{
    [Theory]
    [InlineData("Sql")]
    [InlineData("Simulado")]
    public void Ambos_adaptadores_conviven_y_real_conserva_la_configuracion_del_area(string real)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Clientes:Origen"] = real,
            ["IntegracionesAw:Productos:Origen"] = real,
            ["IntegracionesAw:Productos:OrigenHabilitado"] = "true",
            ["IntegracionesAw:Productos:ArchivoSimulado"] = Path.Combine(AppContext.BaseDirectory, "Productos", "Fixtures", "nominal.json"),
            ["ConnectionStrings:AwClientesDb"] = "Server=demo.invalid;Database=AW_DEMO;Integrated Security=True;Encrypt=True;TrustServerCertificate=False",
            ["ConnectionStrings:AwProductosDb"] = "Server=demo.invalid;Database=AW_DEMO;Integrated Security=True;Encrypt=True;TrustServerCertificate=False",
            ["ConnectionStrings:AwOrigenPgDb"] = "Host=localhost;Database=demo;Username=demo",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddIntegracionesAwClientes(config).AddIntegracionesAwProductos(config).AddIntegracionesAwOrigen(config);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredKeyedService<IAwClientesOrigen>("Real").GetType().Should().Be(real == "Sql"
            ? typeof(AwClientesSqlOrigen) : typeof(AwClientesOrigenSimulado));
        sp.GetRequiredKeyedService<IAwProductosOrigen>("Real").GetType().Should().Be(real == "Sql"
            ? typeof(AwProductosSqlOrigen) : typeof(AwProductosOrigenSimulado));
        sp.GetRequiredKeyedService<IAwClientesOrigen>("Demo").Should().BeOfType<AwClientesPgOrigen>();
        sp.GetRequiredKeyedService<IAwProductosOrigen>("Demo").Should().BeOfType<AwProductosPgOrigen>();
    }
}
