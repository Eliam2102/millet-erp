using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Compras.Application.Almacen;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Compras.UnitTests.Application.Almacen;

/// <summary>
/// G-3: <c>almacen.vale_regularizado.v1</c> (vale de 5 contra una RQ de 8) llega a Compras como una entrega de 5 sobre la
/// línea de la RQ, con la misma forma que la salida con RQ; Compras la proyecta con el handler de entregas (saldo 8 − 5 = 3).
/// </summary>
public sealed class ValeRegularizadoListenerTests
{
    private static readonly Guid RqId = Guid.Parse("00000003-9600-0000-0000-000000000045");
    private static readonly Guid LineaRqId = Guid.Parse("00000003-9700-0000-0000-000000000001");

    [Fact]
    public async Task Vale_regularizado_se_despacha_como_entrega_sobre_la_linea_de_la_RQ()
    {
        var services = new ServiceCollection();
        var dbName = $"compras-vale-regularizado-{Guid.NewGuid():N}";
        var mediador = new Grabador();
        services.AddSingleton<ICurrentEmpresaContext, BypassedEmpresaContext>();
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IMediator>(mediador);
        services.AddScoped(sp => new ComprasDbContext(
            new DbContextOptionsBuilder<ComprasDbContext>().UseInMemoryDatabase(dbName).Options,
            sp.GetRequiredService<ICurrentEmpresaContext>()));
        await using var provider = services.BuildServiceProvider();
        var worker = new AlmacenEventListenerWorker(
            null!, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AlmacenEventListenerWorker>.Instance);
        var eventoId = Guid.NewGuid();
        // Cuerpo tal como lo publica Almacén (ValeRegularizadoIntegrationEvent).
        var cuerpo = $$"""
            {"EmpresaId":"00000003-0000-0000-0000-000000000001","OcurridoEn":"2026-11-05T05:44:00+00:00","SalidaId":"01a1aa3b-3b9c-79ae-82a1-6eb5c71260df",
             "FolioSalida":"M-SAL2026-000001","RqId":"{{RqId}}","EsPorVale":true,
             "Lineas":[{"LineaSalidaId":"01a1aa3b-3ba5-70f3-a9dc-e34152a0a8b3","ArticuloId":"00000003-9100-0000-0000-000000000001","UnidadMedida":"PZA",
                        "Cantidad":5.0000,"CostoUnitarioMxn":118.5,"MontoTotalMxn":592.5,"LineaRqId":"{{LineaRqId}}"}]}
            """;

        await worker.ProcessAsync("almacen.vale_regularizado.v1", eventoId, cuerpo, null, CancellationToken.None);

        var comando = mediador.Comandos.Should().ContainSingle().Which.Should().BeOfType<SalidaRequisicionEnAlmacenCommand>().Subject;
        comando.EventoId.Should().Be(eventoId);
        comando.Payload.RqId.Should().Be(RqId);
        var linea = comando.Payload.Lineas.Should().ContainSingle().Subject;
        linea.LineaRqId.Should().Be(LineaRqId);
        linea.Cantidad.Should().Be(5m);
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }

    private sealed class Grabador : IMediator
    {
        public List<object> Comandos { get; } = [];
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) { Comandos.Add(request); return Task.FromResult(default(TResponse)!); }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest { Comandos.Add(request); return Task.CompletedTask; }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) { Comandos.Add(request); return Task.FromResult<object?>(null); }
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => throw new NotSupportedException();
    }
}
