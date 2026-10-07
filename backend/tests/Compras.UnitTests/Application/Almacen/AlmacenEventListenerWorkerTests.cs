using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// G1.6-d / E13: los eventos informativos de Almacén (nombres reales con
/// guion bajo) se registran en <c>EventoProcesado</c> y no van a dead-letter.
/// </summary>
public sealed class AlmacenEventListenerWorkerTests
{
    [Theory]
    [InlineData("almacen.entrada_inventario.valorada.v1")]
    [InlineData("almacen.ajuste_inventario.aplicado.v1")]
    [InlineData("almacen.devolucion_interna.aplicada.v1")]
    [InlineData("almacen.saldo.proyectado.v1")]
    public async Task Evento_informativo_de_almacen_se_marca_procesado_sin_dead_letter(string eventType)
    {
        var services = new ServiceCollection();
        var dbName = $"compras-almacen-listener-{Guid.NewGuid():N}";
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IMediator>(new NoMediator());
        services.AddScoped(sp => new ComprasDbContext(
            new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(dbName).Options,
            sp.GetRequiredService<ICurrentEmpresaContext>()));
        await using var provider = services.BuildServiceProvider();

        var worker = new AlmacenEventListenerWorker(
            null!, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AlmacenEventListenerWorker>.Instance);
        var eventId = Guid.NewGuid();

        // smbArgs null: si cayera al default (dead-letter) no habría marca.
        await worker.ProcessAsync(eventType, eventId, "{}", null, CancellationToken.None);

        using var scope = provider.CreateScope();
        var marca = await scope.ServiceProvider.GetRequiredService<ComprasDbContext>()
            .EventosProcesados.SingleAsync();
        marca.EventoId.Should().Be(eventId);
        marca.EventoTipo.Should().Be(eventType);
        marca.Observaciones.Should().Contain("Informativo");
    }

    [Fact]
    public async Task Nombres_con_guion_inexistentes_ya_no_se_marcan_como_informativos()
    {
        var services = new ServiceCollection();
        var dbName = $"compras-almacen-listener-{Guid.NewGuid():N}";
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IMediator>(new NoMediator());
        services.AddScoped(sp => new ComprasDbContext(
            new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(dbName).Options,
            sp.GetRequiredService<ICurrentEmpresaContext>()));
        await using var provider = services.BuildServiceProvider();
        var worker = new AlmacenEventListenerWorker(
            null!, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AlmacenEventListenerWorker>.Instance);

        await worker.ProcessAsync("almacen.entrada-inventario.valorada.v1", Guid.NewGuid(), "{}", null, CancellationToken.None);

        using var scope = provider.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().EventosProcesados.CountAsync())
            .Should().Be(0);
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    // El camino informativo no despacha comandos.
    private sealed class NoMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw new NotSupportedException();
    }
}
