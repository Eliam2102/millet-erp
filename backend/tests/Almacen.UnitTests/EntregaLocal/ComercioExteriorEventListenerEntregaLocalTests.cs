using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Almacen.UnitTests.EntregaLocal;

/// <summary>
/// C1.8 — entrega local sin Service Bus: sin <c>ProcessMessageEventArgs</c> (smbArgs
/// null) el listener no puede abandonar el mensaje, así que la falla del handler
/// debe propagarse para que el outbox reintente.
/// </summary>
public sealed class ComercioExteriorEventListenerEntregaLocalTests
{
    [Fact]
    public async Task ConsumirLocalAsync_expone_su_topic_y_relanza_cuando_el_handler_falla()
    {
        await using var provider = CrearProveedor(out var worker);
        ILocalIntegrationEventConsumer consumidor = worker;

        consumidor.TopicName.Should().Be(ComercioExteriorEventListenerWorker.TopicName);
        var act = () => consumidor.ConsumirLocalAsync(
            ExpedienteCostoDistribuidoHandler.EventType, Guid.NewGuid(), "{}", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*handler falló*");
    }

    [Fact]
    public async Task ConsumirLocalAsync_con_payload_invalido_o_evento_no_manejado_no_lanza()
    {
        await using var provider = CrearProveedor(out var worker);
        ILocalIntegrationEventConsumer consumidor = worker;

        await consumidor.ConsumirLocalAsync(
            ExpedienteCostoDistribuidoHandler.EventType, Guid.NewGuid(), "no-es-json", CancellationToken.None);
        await consumidor.ConsumirLocalAsync("comercio_exterior.otro.v1", Guid.NewGuid(), "{}", CancellationToken.None);
    }

    private static ServiceProvider CrearProveedor(out ComercioExteriorEventListenerWorker worker)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IMediator>(new MediatorQueFalla());
        var provider = services.BuildServiceProvider();
        worker = new ComercioExteriorEventListenerWorker(
            null!, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ComercioExteriorEventListenerWorker>.Instance);
        return provider;
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    // Simula una falla transitoria del handler (BD caída, concurrencia, etc.).
    private sealed class MediatorQueFalla : IMediator
    {
        private static InvalidOperationException Falla() => new("El handler falló (simulado).");
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw Falla();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw Falla();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw Falla();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw Falla();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw Falla();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw Falla();
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw Falla();
    }
}
