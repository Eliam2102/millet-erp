using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Oc.Eventos.Cxp;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// C1.8 — entrega local sin Service Bus: sin <c>ProcessMessageEventArgs</c>
/// (smbArgs null) el listener no puede "abandonar" el mensaje, así que la
/// falla del handler debe propagarse para que el outbox reintente.
/// </summary>
public sealed class CxpEventListenerWorkerEntregaLocalTests
{
    [Fact]
    public async Task ProcessAsync_sin_smbArgs_relanza_cuando_el_handler_falla()
    {
        await using var provider = CrearProveedor(out var worker);

        var act = () => worker.ProcessAsync(
            FacturaProveedorRegistradaCommandHandler.EventType, Guid.NewGuid(), "{}", null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*handler falló*");
    }

    [Fact]
    public async Task ConsumirLocalAsync_expone_su_topic_y_relanza_cuando_el_handler_falla()
    {
        await using var provider = CrearProveedor(out var worker);
        ILocalIntegrationEventConsumer consumidor = worker;

        consumidor.TopicName.Should().Be(CxpEventListenerWorker.TopicName);
        var act = () => consumidor.ConsumirLocalAsync(
            FacturaProveedorRegistradaCommandHandler.EventType, Guid.NewGuid(), "{}", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*handler falló*");
    }

    [Fact]
    public async Task ConsumirLocalAsync_con_evento_no_manejado_no_lanza_ni_marca()
    {
        await using var provider = CrearProveedor(out var worker);
        ILocalIntegrationEventConsumer consumidor = worker;

        // En entrega local todos los eventos del topic llegan a todos sus
        // consumidores (no hay filtro SQL de suscripción): lo desconocido se ignora.
        await consumidor.ConsumirLocalAsync("cuentas_por_pagar.otro.v1", Guid.NewGuid(), "{}", CancellationToken.None);

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().EventosProcesados.CountAsync())
            .Should().Be(0);
    }

    private static ServiceProvider CrearProveedor(out CxpEventListenerWorker worker)
    {
        var services = new ServiceCollection();
        var dbName = $"compras-cxp-listener-local-{Guid.NewGuid():N}";
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IMediator>(new MediatorQueFalla());
        services.AddScoped(sp => new ComprasDbContext(
            new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(dbName).Options,
            sp.GetRequiredService<ICurrentEmpresaContext>()));
        var provider = services.BuildServiceProvider();
        worker = new CxpEventListenerWorker(
            null!, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CxpEventListenerWorker>.Instance);
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
        private static InvalidOperationException Falla() => new InvalidOperationException("El handler falló (simulado).");
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw Falla();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw Falla();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw Falla();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw Falla();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw Falla();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw Falla();
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw Falla();
    }
}
