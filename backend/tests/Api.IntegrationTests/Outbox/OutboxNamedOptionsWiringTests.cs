using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Compras.Infrastructure;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.Api.IntegrationTests.Outbox;

/// <summary>
/// Verifica el wiring de <see cref="OutboxPublisherOptions"/> como
/// <i>named options</i> keyed por <c>nameof(TDbContext)</c> (PR B —
/// Integraciones.Aw). Asegura que cada <c>OutboxPublisherWorker&lt;T&gt;</c>
/// publica al topic correcto sin colisionar con el del otro módulo.
///
/// <para>
/// Falla si alguien borra accidentalmente el wiring de las named options
/// en <c>Program.cs</c>, o si el binding de configuración pierde la
/// sección por módulo (<c>Compras:Outbox</c> / <c>IntegracionesAw:Outbox</c>).
/// </para>
/// </summary>
public sealed class OutboxNamedOptionsWiringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OutboxNamedOptionsWiringTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Compras_OptionsName_ResuelveTopicCompras()
    {
        var monitor = _factory.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>();

        var options = monitor.Get(nameof(ComprasDbContext));

        Assert.Equal("compras-events", options.ServiceBusTopicName);
    }

    [Fact]
    public void IntegracionesAw_OptionsName_ResuelveTopicIntegracionesAw()
    {
        var monitor = _factory.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>();

        var options = monitor.Get(nameof(IntegracionesAwDbContext));

        Assert.Equal("integraciones-aw-events", options.ServiceBusTopicName);
    }

    [Fact]
    public void NamedOptions_NoColisionan_EntreModulos()
    {
        var monitor = _factory.Services.GetRequiredService<IOptionsMonitor<OutboxPublisherOptions>>();

        var compras = monitor.Get(nameof(ComprasDbContext));
        var aw = monitor.Get(nameof(IntegracionesAwDbContext));

        Assert.NotEqual(compras.ServiceBusTopicName, aw.ServiceBusTopicName);
    }
}
