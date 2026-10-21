using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Millet.Api.Infrastructure;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.UnitTests;

/// <summary>
/// C1.8 — registro de la entrega local de eventos (sin Service Bus).
/// </summary>
public sealed class EntregaLocalIntegrationEventsTests
{
    private static readonly string[] TopicsEsperados =
    [
        "almacen-events", "compras-events", "cuentas-por-cobrar-events",
        "cuentas-por-pagar-events", "facturacion-events", "tesoreria-events",
    ];

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void Fuera_de_Development_con_la_opcion_activa_falla_al_arrancar(string ambiente)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddEntregaLocalIntegrationEvents(Config(habilitada: "true"), new Ambiente(ambiente)));

        Assert.Contains("C1.8", ex.Message);
        Assert.Contains("solo se permite en Development", ex.Message);
    }

    [Fact]
    public void En_Development_con_la_opcion_activa_registra_el_sender_local_y_los_12_consumidores()
    {
        var services = new ServiceCollection().AddLogging();

        var registrado = services.AddEntregaLocalIntegrationEvents(Config(habilitada: "true"), new Ambiente(Environments.Development));

        Assert.True(registrado);
        using var sp = services.BuildServiceProvider();
        Assert.IsType<LocalDispatchIntegrationEventBusSender>(sp.GetRequiredService<IIntegrationEventBusSender>());
        var consumidores = sp.GetServices<ILocalIntegrationEventConsumer>().ToList();
        Assert.Equal(12, consumidores.Count);
        Assert.Equal(12, consumidores.Select(c => c.GetType()).Distinct().Count());
        Assert.Equal(
            TopicsEsperados,
            consumidores.Select(c => c.TopicName).Distinct().Order().ToArray());
        // No se registran como HostedService de Service Bus (sin ServiceBusClient).
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService)
            && d.ImplementationType?.Name.EndsWith("EventListenerWorker", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Con_la_opcion_apagada_no_registra_nada(string? habilitada)
    {
        var services = new ServiceCollection();

        // Aunque el ambiente sea Production: apagada nunca lanza.
        var registrado = services.AddEntregaLocalIntegrationEvents(Config(habilitada), new Ambiente(Environments.Production));

        Assert.False(registrado);
        Assert.Empty(services);
    }

    [Fact]
    public void Con_conexion_a_Service_Bus_la_opcion_solo_registra_un_aviso()
    {
        var services = new ServiceCollection();

        services.AdvertirSiEntregaLocalIgnorada(Config(habilitada: "true"));

        var unico = Assert.Single(services);
        Assert.Equal(typeof(IHostedService), unico.ServiceType);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IIntegrationEventBusSender));
    }

    private static IConfiguration Config(string? habilitada) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [EntregaLocalIntegrationEventsExtensions.ClaveHabilitada] = habilitada })
            .Build();

    private sealed class Ambiente(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "Millet.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
