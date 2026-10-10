using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.Integraciones.Aw.Infrastructure.Clientes;
using Millet.Integraciones.Aw.Infrastructure.Productos;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.OrigenPg;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Origen;

public sealed class AwOrigenDiTests
{
    private sealed class Real : IAwOrigenActivo
    {
        public Task<AwOrigenEstado> LeerAsync(CancellationToken ct) => Task.FromResult(
            new AwOrigenEstado("Real", false, false, "Sql", "Simulado", null, null, 1));
    }

    [Fact]
    public async Task Real_sql_sin_cadena_resuelve_selector_pero_rechaza_la_lectura_sin_fallback()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["IntegracionesAw:Clientes:Origen"] = "Sql",
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddIntegracionesAwClientes(config).AddIntegracionesAwOrigen(config);
        services.AddScoped<IAwOrigenActivo, Real>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var origen = scope.ServiceProvider.GetRequiredService<IAwClientesOrigen>();
        origen.Should().BeOfType<AwOrigenSelectores>();
        var error = await Assert.ThrowsAsync<AwClientesSyncException>(() => origen.LeerPorReferenciaAsync("1", default));
        error.Code.Should().Be("origen_sin_configurar");
        error.Message.Should().Be("Origen 'Sql' sin adaptador: falta ConnectionStrings:AwClientesDb.");
        await Assert.ThrowsAsync<AwClientesSyncException>(() => origen.LeerPaginaAsync(null, 10, default));
    }

    [Fact]
    public async Task Demo_resuelta_sin_cadena_da_error_claro_y_lee_la_configuracion_al_usarse()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection().AddLogging();
        services.AddIntegracionesAwClientes(config).AddIntegracionesAwProductos(config).AddIntegracionesAwOrigen(config);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        // Resueltos antes de configurar la conexión: no deben capturar una cadena vacía.
        var clientes = sp.GetRequiredKeyedService<IAwClientesOrigen>("Demo");
        var productos = sp.GetRequiredKeyedService<IAwProductosOrigen>("Demo");
        var fabrica = sp.GetRequiredKeyedService<IIntegracionSqlConnectionFactory>("Demo");
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => productos.LeerPaginaAsync(null, 10, default));
        error.Message.Should().Be("La copia de demo de A+W no está configurada en este ambiente");
        await Assert.ThrowsAsync<BusinessRuleException>(() => clientes.LeerPaginaAsync(null, 10, default));

        config["ConnectionStrings:AwOrigenPgDb"] = "Host=localhost;Database=demo;Username=demo";
        using (var conexion = fabrica.CreateConnection()) conexion.Database.Should().Be("demo");
        config["ConnectionStrings:AwOrigenPgDb"] = "Host=localhost;Database=otra_demo;Username=demo";
        using (var conexion = fabrica.CreateConnection()) conexion.Database.Should().Be("otra_demo");
        config["ConnectionStrings:AwOrigenPgDb"] = "";
        await Assert.ThrowsAsync<BusinessRuleException>(() => productos.LeerPaginaAsync(null, 10, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("@Microsoft.KeyVault(SecretUri=pendiente)")]
    public void Fabrica_demo_rechaza_cadenas_ausentes_o_no_resueltas(string cadena)
    {
        var error = Assert.Throws<BusinessRuleException>(() => new AwOrigenPg.Fabrica(cadena).CreateConnection());
        error.Code.Should().Be("AW_DEMO_NO_CONFIGURADA");
        error.Message.Should().Be("La copia de demo de A+W no está configurada en este ambiente");
    }

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
