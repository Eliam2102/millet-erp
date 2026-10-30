using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Ingesta.ProcesarNotaCreditoAw;
using Millet.Facturacion.Application.Ingesta.ProcesarSolicitudAw;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Ingesta;

/// <summary>
/// U1.4: el worker de la cola A+W barre cada 15 s y deja en el log la
/// latencia cola → ERP de cada aviso (insumo de la medición de R14).
/// </summary>
public sealed class AwSolicitudesWorkerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);

    private sealed class FakeReader(IReadOnlyList<SolicitudAw> pendientes) : IAwSolicitudesReader
    {
        public Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(int max, CancellationToken ct) =>
            Task.FromResult(pendientes);

        public Task<LecturaPedidoAw> LeerDatosPedidoAsync(string numeroPedido, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    public sealed class FakeProcesarHandler : IRequestHandler<ProcesarSolicitudAwCommand, ProcesarSolicitudAwResponse>
    {
        public static readonly List<string> Procesados = [];

        public Task<ProcesarSolicitudAwResponse> Handle(ProcesarSolicitudAwCommand request, CancellationToken cancellationToken)
        {
            lock (Procesados) Procesados.Add($"{request.Solicitud.NumeroPedido}/v{request.Solicitud.Version}");
            return Task.FromResult(new ProcesarSolicitudAwResponse(ResultadoSolicitudAw.Aplicada, Guid.NewGuid(), null));
        }
    }

    private sealed class FakeNotas(IReadOnlyList<SolicitudNotaCreditoAw> pendientes) : IAwNotasCreditoPort
    {
        public Task<IReadOnlyList<SolicitudNotaCreditoAw>> LeerNotasPendientesAsync(int max, CancellationToken cancellationToken) =>
            Task.FromResult(pendientes);
        public Task EscribirAcuseAsync(AcuseNotaCreditoAw acuse, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class FakeProcesarNotaHandler : IRequestHandler<ProcesarNotaCreditoAwCommand, ProcesarNotaCreditoAwResponse>
    {
        public static readonly List<string> Procesadas = [];

        public Task<ProcesarNotaCreditoAwResponse> Handle(ProcesarNotaCreditoAwCommand request, CancellationToken cancellationToken)
        {
            lock (Procesadas) Procesadas.Add($"{request.Solicitud.NumeroNota}/v{request.Solicitud.Version}");
            if (request.Solicitud.NumeroNota == "NC-FALLA") throw new InvalidOperationException("falla de prueba");
            return Task.FromResult(new ProcesarNotaCreditoAwResponse(ResultadoSolicitudAw.Aplicada, Guid.NewGuid(), null));
        }
    }

    private sealed class MonitorFijo(AwSolicitudesOptions valor) : IOptionsMonitor<AwSolicitudesOptions>
    {
        public AwSolicitudesOptions CurrentValue => valor;
        public AwSolicitudesOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<AwSolicitudesOptions, string?> listener) => null;
    }

    private sealed class CapturaLogger : ILogger<AwSolicitudesWorker>
    {
        public readonly List<string> Mensajes = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Mensajes.Add(formatter(state, exception));
    }

    [Fact]
    public void Intervalo_por_defecto_es_15_segundos_para_cumplir_menos_de_un_minuto()
    {
        new AwSolicitudesOptions().IntervalSeconds.Should().Be(15);
    }

    [Fact]
    public async Task Tick_procesa_en_orden_y_registra_la_latencia_cola_ERP_de_cada_aviso()
    {
        var solicitudes = new List<SolicitudAw>
        {
            new(Guid.NewGuid(), "5002", OperacionAw.Alta, 1, Ahora.AddSeconds(-20)),
            new(Guid.NewGuid(), "5001", OperacionAw.Modificacion, 2, Ahora.AddSeconds(-4)),
            new(Guid.NewGuid(), "5001", OperacionAw.Alta, 1, Ahora.AddSeconds(-12)),
        };

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(null));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddSingleton<IAwSolicitudesReader>(new FakeReader(solicitudes));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FakeProcesarHandler>());
        var sp = services.BuildServiceProvider();

        var logger = new CapturaLogger();
        using var signal = new AwSolicitudesTickSignal();
        var worker = new AwSolicitudesWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MonitorFijo(new AwSolicitudesOptions { EmpresaId = Guid.NewGuid() }),
            signal,
            logger);

        FakeProcesarHandler.Procesados.Clear();
        var procesadas = await worker.TickAsync(CancellationToken.None);

        procesadas.Should().Be(3);
        FakeProcesarHandler.Procesados.Should().Equal("5001/v1", "5001/v2", "5002/v1");
        logger.Mensajes.Should().Contain(m => m.Contains("Pedido 5001 v1") && m.Contains("latencia cola→ERP 12000 ms"));
        logger.Mensajes.Should().Contain(m => m.Contains("Pedido 5002 v1") && m.Contains("latencia cola→ERP 20000 ms"));
        logger.Mensajes.Should().Contain(m => m.Contains("promedio 12000 ms") && m.Contains("máxima 20000 ms"));
    }

    [Fact]
    public async Task Tick_de_notas_de_credito_procesa_en_orden_y_una_que_falla_no_frena_las_demas()
    {
        SolicitudNotaCreditoAw Nota(string numero, long version) =>
            new(Guid.NewGuid(), numero, version, "AW-1", null, "CLI-1", "Bonificacion", 100m, null, Ahora);
        var notas = new List<SolicitudNotaCreditoAw> { Nota("NC-2", 1), Nota("NC-FALLA", 1), Nota("NC-1", 2), Nota("NC-1", 1) };

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(null));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        services.AddSingleton<IAwNotasCreditoPort>(new FakeNotas(notas));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<FakeProcesarNotaHandler>());
        var sp = services.BuildServiceProvider();

        using var signal = new AwSolicitudesTickSignal();
        var worker = new AwSolicitudesWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            new MonitorFijo(new AwSolicitudesOptions { EmpresaId = Guid.NewGuid() }), signal, new CapturaLogger());

        FakeProcesarNotaHandler.Procesadas.Clear();
        var procesadas = await worker.TickNotasCreditoAsync(CancellationToken.None);

        procesadas.Should().Be(3);
        FakeProcesarNotaHandler.Procesadas.Should().Equal("NC-1/v1", "NC-1/v2", "NC-2/v1", "NC-FALLA/v1");
    }
}
