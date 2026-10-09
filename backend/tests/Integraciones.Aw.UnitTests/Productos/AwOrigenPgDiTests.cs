using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Productos;

namespace Millet.Integraciones.Aw.UnitTests.Productos;

/// <summary>DI del origen de demo en PostgreSQL: se activa por configuración y solo con connection string.</summary>
public sealed class AwOrigenPgDiTests
{
    private static ServiceProvider Proveedor(string? cs)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Clientes:Origen"] = "Postgres",
            ["IntegracionesAw:Productos:OrigenHabilitado"] = "true",
            ["IntegracionesAw:Productos:Origen"] = "Postgres",
            ["ConnectionStrings:AwOrigenPgDb"] = cs,
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddIntegracionesAwClientes(config);
        services.AddIntegracionesAwProductos(config);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Con_connection_string_registra_los_dos_origenes_postgres()
    {
        using var sp = Proveedor("Host=localhost;Database=millet_aw_origen;Username=u;Password=p");
        sp.GetRequiredService<IAwClientesOrigen>().Should().BeOfType<AwClientesPgOrigen>();
        sp.GetRequiredService<IAwProductosOrigen>().Should().BeOfType<AwProductosPgOrigen>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sin_connection_string_no_registra_origen(string? cs)
    {
        using var sp = Proveedor(cs);
        sp.GetService<IAwClientesOrigen>().Should().BeNull();
        sp.GetService<IAwProductosOrigen>().Should().BeNull();
    }
}
