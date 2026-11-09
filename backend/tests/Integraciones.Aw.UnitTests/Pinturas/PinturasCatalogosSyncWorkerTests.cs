using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Application.Workers;
using Millet.Integraciones.Aw.Infrastructure.Origen;
using Millet.Integraciones.Aw.Infrastructure.Pedidos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

public class PinturasCatalogosSyncWorkerTests
{
    private readonly FakeExtractor _extractor = new();
    private readonly FakePublisher _publisher = new();
    private readonly TestEmpresaContext _empresaContext = new();
    private readonly TestAuditOriginContext _auditOriginContext = new();

    private ServiceProvider CrearServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(_empresaContext);
        services.AddSingleton<IAuditOriginContext>(_auditOriginContext);
        services.AddSingleton<IPinturasCatalogosExtractor>(_extractor);
        services.AddSingleton<IPinturasCatalogosPublisher>(_publisher);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Constructor_ArgumentosNulos_LanzaArgumentNullException()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var optionsMonitor = new TestOptionsMonitor<PinturasCatalogosOptions>(new PinturasCatalogosOptions());
        var logger = NullLogger<PinturasCatalogosSyncWorker>.Instance;

        var act1 = () => new PinturasCatalogosSyncWorker(null!, optionsMonitor, logger);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");

        var act2 = () => new PinturasCatalogosSyncWorker(scopeFactory, null!, logger);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");

        var act3 = () => new PinturasCatalogosSyncWorker(scopeFactory, optionsMonitor, null!);
        act3.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task SincronizarUnaVezAsync_FlujoExitoso_ExtraePublicaYRetornaResultado()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = true };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        var batchEsperado = new PinturasCatalogosBatch(
            [new FilaPinturasCliente(101, "Cliente Uno", Activo: true)],
            [new FilaPinturasServicio("SER01", "Pintura Horneada", Activo: true)],
            [new FilaPinturasObra("OB01", "Torre Uno", FilaPinturasObra.OrigenPinturas, null, Activo: true)],
            [new FilaPinturasEmpleado("EMP01", "Juan Perez", Activo: true)]);

        var fechaPublicacion = DateTimeOffset.UtcNow;
        var resultadoEsperado = new PinturasPublicacionResultado(1, 1, 1, 1, fechaPublicacion);

        _extractor.BatchADevolver = batchEsperado;
        _publisher.ResultadoADevolver = resultadoEsperado;

        var resultado = await worker.SincronizarUnaVezAsync(CancellationToken.None);

        resultado.Should().Be(resultadoEsperado);
        _extractor.FueLlamado.Should().BeTrue();
        _publisher.FueLlamado.Should().BeTrue();
        _publisher.BatchRecibido.Should().BeSameAs(batchEsperado);
    }

    [Fact]
    public async Task SincronizarUnaVezAsync_EstableceAuditOriginYBypassEmpresa()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = true };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        bool bypassActivoDuranteExtraccion = false;
        string? originDuranteExtraccion = null;

        _extractor.EnExtraer = () =>
        {
            bypassActivoDuranteExtraccion = _empresaContext.IsBypassed;
            originDuranteExtraccion = _auditOriginContext.Origin;
        };

        bool bypassActivoDurantePublicacion = false;
        string? originDurantePublicacion = null;

        _publisher.EnPublicar = () =>
        {
            bypassActivoDurantePublicacion = _empresaContext.IsBypassed;
            originDurantePublicacion = _auditOriginContext.Origin;
        };

        await worker.SincronizarUnaVezAsync(CancellationToken.None);

        bypassActivoDuranteExtraccion.Should().BeTrue();
        originDuranteExtraccion.Should().Be(nameof(PinturasCatalogosSyncWorker));

        bypassActivoDurantePublicacion.Should().BeTrue();
        originDurantePublicacion.Should().Be(nameof(PinturasCatalogosSyncWorker));

        _empresaContext.BypassFueLlamado.Should().BeTrue();
        _empresaContext.IsBypassed.Should().BeFalse(); // Al terminar el scope se libera
        _auditOriginContext.HistorialOrigins.Should().Contain(nameof(PinturasCatalogosSyncWorker));
        _auditOriginContext.Origin.Should().BeNull(); // Al terminar el scope se libera
    }

    [Fact]
    public async Task SincronizarUnaVezAsync_HabilitadoFalso_OmiteSincronizacionYDevuelveVacio()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = false };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        var resultado = await worker.SincronizarUnaVezAsync(CancellationToken.None);

        resultado.TotalPublicados.Should().Be(0);
        _extractor.FueLlamado.Should().BeFalse();
        _publisher.FueLlamado.Should().BeFalse();
        _empresaContext.BypassFueLlamado.Should().BeFalse();
    }

    [Fact]
    public async Task SincronizarUnaVezAsync_ExtractorLanzaExcepcion_EsResilienteYRetornaVacio()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = true };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        _extractor.ExcepcionALanzar = new InvalidOperationException("Falla simulada en base de datos ERP.");

        var resultado = await worker.SincronizarUnaVezAsync(CancellationToken.None);

        resultado.TotalPublicados.Should().Be(0);
        _extractor.FueLlamado.Should().BeTrue();
        _publisher.FueLlamado.Should().BeFalse();
    }

    [Fact]
    public async Task SincronizarUnaVezAsync_PublisherLanzaExcepcion_EsResilienteYRetornaVacio()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = true };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        _publisher.ExcepcionALanzar = new TimeoutException("Falla simulada de timeout hacia MILLET_INTEGRACION.");

        var resultado = await worker.SincronizarUnaVezAsync(CancellationToken.None);

        resultado.TotalPublicados.Should().Be(0);
        _extractor.FueLlamado.Should().BeTrue();
        _publisher.FueLlamado.Should().BeTrue();
    }

    [Fact]
    public async Task SincronizarUnaVezAsync_TokenCancelado_PropagaOperationCanceledException()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = true };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => worker.SincronizarUnaVezAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteAsync_RespetaCancelacionYSeDetiene()
    {
        var sp = CrearServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var options = new PinturasCatalogosOptions { Habilitado = true, IntervaloMinutos = 1 };
        var worker = new PinturasCatalogosSyncWorker(
            scopeFactory,
            new TestOptionsMonitor<PinturasCatalogosOptions>(options),
            NullLogger<PinturasCatalogosSyncWorker>.Instance);

        using var cts = new CancellationTokenSource();

        var extraccionIniciada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _extractor.EnExtraer = () => extraccionIniciada.SetResult();

        await worker.StartAsync(cts.Token);
        try
        {
            await extraccionIniciada.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }

        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task NoOpPinturasCatalogosPublisher_DevuelveResultadoVacio()
    {
        var publisher = new NoOpPinturasCatalogosPublisher();
        var batch = new PinturasCatalogosBatch(
            [new FilaPinturasCliente(1, "Cliente Demo")],
            [new FilaPinturasServicio("S1", "Servicio Demo")],
            [new FilaPinturasObra("O1", "Obra Demo")],
            [new FilaPinturasEmpleado("E1", "Empleado Demo")]);

        var resultado = await publisher.PublicarAsync(batch, CancellationToken.None);

        resultado.Should().NotBeNull();
        resultado.TotalPublicados.Should().Be(0);
        resultado.ClientesPublicados.Should().Be(0);
        resultado.ServiciosPublicados.Should().Be(0);
        resultado.ObrasPublicadas.Should().Be(0);
        resultado.EmpleadosPublicados.Should().Be(0);
        NoOpPinturasCatalogosPublisher.Instance.Should().NotBeNull();
    }

    [Fact]
    public void DependencyInjection_AddPinturasPedidos_RegistraOpcionesExtractorYObrasSource()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IntegracionesAw:Pinturas:Catalogos:IntervaloMinutos"] = "45",
                ["IntegracionesAw:Pinturas:Catalogos:BatchSize"] = "250",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<CompartidoDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton<ICurrentEmpresaContext>(new TestEmpresaContext());

        PedidosDependencyInjection.AddPinturasPedidos(services, config);

        var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<PinturasCatalogosOptions>>().Value;
        options.IntervaloMinutos.Should().Be(45);
        options.BatchSize.Should().Be(250);

        var obrasSource = sp.GetRequiredService<IPinturasObrasSource>();
        obrasSource.Should().BeOfType<NoOpPinturasObrasSource>();

        var extractor = sp.GetRequiredService<IPinturasCatalogosExtractor>();
        extractor.Should().BeOfType<PinturasCatalogosExtractor>();
    }

    [Fact]
    public void DependencyInjection_AddIntegracionesAwPedidosAdapters_RegistraWorkerYPublisher()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AwIntegracionDb"] = "Server=tcp:localhost,1433;Database=test;User Id=sa;Password=test;TrustServerCertificate=True;",
                ["IntegracionesAw:Pinturas:Catalogos:IntervaloMinutos"] = "20"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, SystemClock>();
        services.AddDbContext<CompartidoDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton<ICurrentEmpresaContext>(new TestEmpresaContext());

        services.AddIntegracionesAwPedidosAdapters(config);

        // Verifica que PinturasCatalogosSyncWorker se registra como IHostedService
        services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PinturasCatalogosSyncWorker))
            .Should().BeTrue();

        // Verifica que IPinturasCatalogosPublisher está registrado
        services.Any(d => d.ServiceType == typeof(IPinturasCatalogosPublisher))
            .Should().BeTrue();
    }

    [Fact]
    public void DependencyInjection_AddIntegracionesAwOrigen_RegistraServiciosDemo()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AwPgIntegracionDb"] = "Host=localhost;Database=test;Username=postgres;Password=test;",
                ["IntegracionesAw:Pinturas:Catalogos:IntervaloMinutos"] = "15"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock, SystemClock>();
        services.AddDbContext<CompartidoDbContext>(opt => opt.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton<ICurrentEmpresaContext>(new TestEmpresaContext());

        services.AddIntegracionesAwOrigen(config);

        // Verifica que los servicios keyed "Demo" existen para publisher y extractor
        services.Any(d => d.ServiceType == typeof(IPinturasCatalogosPublisher) && d.IsKeyedService && Equals(d.ServiceKey, "Demo"))
            .Should().BeTrue();

        services.Any(d => d.ServiceType == typeof(IPinturasCatalogosExtractor) && d.IsKeyedService && Equals(d.ServiceKey, "Demo"))
            .Should().BeTrue();
    }

    private sealed class TestEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed { get; private set; }
        public bool BypassFueLlamado { get; private set; }

        public IDisposable Bypass()
        {
            BypassFueLlamado = true;
            IsBypassed = true;
            return new Scope(() => IsBypassed = false);
        }

        private sealed class Scope(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    private sealed class TestAuditOriginContext : IAuditOriginContext
    {
        public string? Origin { get; private set; }
        public List<string> HistorialOrigins { get; } = [];

        public IDisposable SetOrigin(string origin)
        {
            Origin = origin;
            HistorialOrigins.Add(origin);
            return new Scope(() => Origin = null);
        }

        private sealed class Scope(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }

    private sealed class FakeExtractor : IPinturasCatalogosExtractor
    {
        public PinturasCatalogosBatch BatchADevolver { get; set; } = PinturasCatalogosBatch.Empty;
        public bool FueLlamado { get; private set; }
        public Exception? ExcepcionALanzar { get; set; }
        public Action? EnExtraer { get; set; }

        public Task<PinturasCatalogosBatch> ExtraerAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FueLlamado = true;
            EnExtraer?.Invoke();
            if (ExcepcionALanzar != null)
            {
                throw ExcepcionALanzar;
            }
            return Task.FromResult(BatchADevolver);
        }
    }

    private sealed class FakePublisher : IPinturasCatalogosPublisher
    {
        public PinturasCatalogosBatch? BatchRecibido { get; private set; }
        public bool FueLlamado { get; private set; }
        public PinturasPublicacionResultado ResultadoADevolver { get; set; } = PinturasPublicacionResultado.Vacio();
        public Exception? ExcepcionALanzar { get; set; }
        public Action? EnPublicar { get; set; }

        public Task<PinturasPublicacionResultado> PublicarAsync(PinturasCatalogosBatch batch, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FueLlamado = true;
            BatchRecibido = batch;
            EnPublicar?.Invoke();
            if (ExcepcionALanzar != null)
            {
                throw ExcepcionALanzar;
            }
            return Task.FromResult(ResultadoADevolver);
        }
    }

    private sealed class TestOptionsMonitor<T>(T initialValue) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; set; } = initialValue;
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
