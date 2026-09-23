using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Almacen.Domain;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.Workers;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure;

namespace Millet.Almacen.UnitTests.Reorden;

/// <summary>
/// Tests del doble interruptor del <see cref="ReordenWorker"/>:
/// (1) kill-switch de config (<c>ReordenWorker:Disabled</c>) — el servicio
/// termina al arrancar sin crear scopes ni tocar BD; (2) interruptor
/// operativo (<c>AlmacenSettings.ReabastoAutomaticoActivo</c>, por la
/// empresa del usuario de servicio) — consultado en cada ciclo ANTES del
/// advisory lock: apagado o usuario sin sembrar → el ciclo se salta sin
/// invocar <c>GenerarBorradoresReordenCommand</c>.
///
/// <para>El camino "flag ON ejecuta el barrido completo" atraviesa el
/// advisory lock de PG (no reproducible con InMemory); lo cubre el test de
/// integración <c>ReordenWorkerToggleTests</c> de Api.IntegrationTests.</para>
/// </summary>
public class ReordenWorkerToggleTests
{
    private static readonly Guid EmpresaMotor = Guid.NewGuid();

    [Fact]
    public async Task KillSwitch_de_config_termina_sin_crear_scopes_ni_tocar_BD()
    {
        var scopeFactory = new ScopeFactoryQueExplota();
        var worker = new ReordenWorker(
            scopeFactory,
            Options.Create(new ReordenWorkerOptions { Disabled = true }),
            NullLogger<ReordenWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!; // termina solo, sin loop

        scopeFactory.Creaciones.Should().Be(0); // ni un scope: BD intacta
    }

    [Fact]
    public async Task Flag_operativo_apagado_salta_el_ciclo_sin_invocar_el_command()
    {
        var mediator = new FakeMediator();
        // Fila existente con flag=false (el default del seed).
        var worker = CrearWorker(mediator, flagEnBd: false, out _);

        var resultado = await worker.EjecutarBarridoAsync(CancellationToken.None);

        resultado.Should().BeNull();
        mediator.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Sin_fila_de_settings_equivale_a_apagado()
    {
        var mediator = new FakeMediator();
        var worker = CrearWorker(mediator, flagEnBd: null, out _);

        var resultado = await worker.EjecutarBarridoAsync(CancellationToken.None);

        resultado.Should().BeNull();
        mediator.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Usuario_de_servicio_sin_sembrar_salta_el_ciclo()
    {
        var mediator = new FakeMediator();
        var worker = CrearWorker(mediator, flagEnBd: true, out _, conUsuarioServicio: false);

        var resultado = await worker.EjecutarBarridoAsync(CancellationToken.None);

        resultado.Should().BeNull();
        mediator.Sends.Should().Be(0);
    }

    [Fact]
    public async Task Flag_encendido_pasa_el_gate_y_avanza_hacia_el_barrido()
    {
        // Con InMemory el ciclo no puede atravesar el advisory lock relacional:
        // que truene EXACTAMENTE ahí (y no antes) prueba que el gate del flag
        // dejó pasar. El happy-path completo vive en Api.IntegrationTests.
        var mediator = new FakeMediator();
        var worker = CrearWorker(mediator, flagEnBd: true, out _);

        var act = () => worker.EjecutarBarridoAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(); // TX relacional sobre InMemory
        mediator.Sends.Should().Be(0); // aún no llegaba al command
    }

    // ─── Infra de test ───

    private static ReordenWorker CrearWorker(
        FakeMediator mediator,
        bool? flagEnBd,
        out AlmacenDbContext db,
        bool conUsuarioServicio = true)
    {
        var opts = new DbContextOptionsBuilder<AlmacenDbContext>()
            .UseInMemoryDatabase($"almacen-worker-toggle-{Guid.NewGuid():N}")
            .Options;
        db = new AlmacenDbContext(opts, new BypassedEmpresaContext());
        db.Database.EnsureCreated();
        if (flagEnBd is bool valor)
        {
            db.AlmacenSettings.Add(new AlmacenSettings(Guid.CreateVersion7(), EmpresaMotor, valor));
            db.SaveChanges();
        }

        var usuarioServicio = conUsuarioServicio
            ? new UsuarioServicioLectura(Guid.NewGuid(), EmpresaMotor, Activo: true)
            : null;

        var services = new ServiceCollection();
        var dbCapturada = db;
        services.AddScoped<IMediator>(_ => mediator);
        services.AddScoped<ICurrentEmpresaContext>(_ => new BypassedEmpresaContext());
        services.AddScoped<IAuditOriginContext, AuditOriginContext>();
        services.AddScoped(_ => dbCapturada);
        services.AddScoped<IUsuarioServicioReadPort>(_ => new FakeUsuarioServicioPort(usuarioServicio));

        return new ReordenWorker(
            new ServiceCollectionScopeFactory(services.BuildServiceProvider()),
            Options.Create(new ReordenWorkerOptions { Disabled = false }),
            NullLogger<ReordenWorker>.Instance);
    }

    private sealed class FakeUsuarioServicioPort : IUsuarioServicioReadPort
    {
        private readonly UsuarioServicioLectura? _usuario;
        public FakeUsuarioServicioPort(UsuarioServicioLectura? usuario) => _usuario = usuario;
        public Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_usuario);
    }

    private sealed class FakeMediator : IMediator
    {
        public int Sends { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Sends++;
            return Task.FromResult(default(TResponse)!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            Sends++;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Sends++;
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;
    }

    private sealed class ServiceCollectionScopeFactory : IServiceScopeFactory
    {
        private readonly ServiceProvider _provider;
        public ServiceCollectionScopeFactory(ServiceProvider provider) => _provider = provider;
        public IServiceScope CreateScope() => _provider.CreateScope();
    }

    private sealed class ScopeFactoryQueExplota : IServiceScopeFactory
    {
        public int Creaciones { get; private set; }
        public IServiceScope CreateScope()
        {
            Creaciones++;
            throw new InvalidOperationException("El kill-switch no debe crear scopes.");
        }
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
