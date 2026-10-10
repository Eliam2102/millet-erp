using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
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

    [Theory]
    [InlineData("Postgres", "Host=localhost;Database=millet_aw_origen;Username=u;Password=p", true)]
    [InlineData("Postgres", "", false)]
    [InlineData(null, "Host=localhost;Database=millet_aw_origen;Username=u;Password=p", false)]
    public void Pedidos_usan_el_origen_postgres_solo_con_origen_y_connection_string(string? origen, string cs, bool esperado)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Pedidos:Origen"] = origen,
            ["ConnectionStrings:AwOrigenPgDb"] = cs,
            ["ConnectionStrings:AwIntegracionDb"] = "Server=SER-DATA;Database=MILLET_INTEGRACION",
        }).Build();
        PedidosDependencyInjection.OrigenPostgres(config).Should().Be(esperado);

        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(config);
        services.AddIntegracionesAwPedidosAdapters(config);
        using var sp = services.BuildServiceProvider();
        var fabrica = sp.GetRequiredService<IIntegracionSqlConnectionFactory>();
        if (esperado) fabrica.Should().BeOfType<AwOrigenPg.Fabrica>();
        else fabrica.Should().BeOfType<IntegracionSqlConnectionFactory>();
    }
}
