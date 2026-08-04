using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ingesta;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Workers;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.UnitTests.Ingesta;

/// <summary>
/// PR5 (ADR-0048 D3): write-back diferido hacia la tabla-puente — semántica
/// de dominio en <see cref="IngestaControl"/> y drenado por
/// <see cref="WriteBackResultadoWorker"/>.
/// </summary>
public sealed class WriteBackTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();

    private static IngestaControl Control() => IngestaControl.Crear(
        EmpresaId, OrigenPedido.Aw, "10432218", "hash", 1,
        EstadoIngesta.Importado, Guid.NewGuid(), Ahora);

    // ===== Dominio =====

    [Fact]
    public void SolicitarWriteBackEstado_marca_pendiente_por_pedido()
    {
        var control = Control();
        control.SolicitarWriteBackEstado("Facturado", "UUID-1", Ahora);

        control.WriteBackPendiente.Should().BeTrue();
        control.WriteBackSolicitudId.Should().BeNull(); // por-pedido → Guid.Empty en el adapter
        control.WriteBackEstado.Should().Be("Facturado");
        control.WriteBackUuid.Should().Be("UUID-1");
        control.WriteBackIntentos.Should().Be(0);
    }

    [Fact]
    public void SolicitarReintentoWriteBack_conserva_solicitud_y_resultado()
    {
        var control = Control();
        var solicitudId = Guid.NewGuid();
        control.SolicitarReintentoWriteBack(
            solicitudId, ResultadoSolicitudAw.Rechazada, "cliente no existe", null, Ahora);

        control.WriteBackPendiente.Should().BeTrue();
        control.WriteBackSolicitudId.Should().Be(solicitudId);
        control.WriteBackResultado.Should().Be(ResultadoSolicitudAw.Rechazada);
        control.WriteBackMotivo.Should().Be("cliente no existe");
    }

    [Fact]
    public void ConfirmarWriteBack_limpia_pendiente_y_fallos_incrementan_intentos()
    {
        var control = Control();
        control.SolicitarWriteBackEstado("Facturado", "UUID-1", Ahora);

        control.RegistrarFalloWriteBack("HC caída", Ahora);
        control.RegistrarFalloWriteBack("HC caída", Ahora);
        control.WriteBackIntentos.Should().Be(2);
        control.WriteBackUltimoError.Should().Be("HC caída");
        control.WriteBackPendiente.Should().BeTrue();

        control.ConfirmarWriteBack(Ahora);
        control.WriteBackPendiente.Should().BeFalse();
        control.WriteBackUltimoError.Should().BeNull();
        control.WriteBackAt.Should().Be(Ahora);
    }

    // ===== Worker =====

    private sealed class ThrowingWriteBackPort : IAwWriteBackPort
    {
        public int Llamadas { get; private set; }
        public Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken ct)
        {
            Llamadas++;
            throw new InvalidOperationException("HC caída");
        }
    }

    private static (ServiceProvider Sp, FakeAwWriteBackPort Port) ArmarWorkerServices(
        string dbName, IAwWriteBackPort? port = null)
    {
        var services = new ServiceCollection();
        var fake = new FakeAwWriteBackPort();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(EmpresaId));
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IAwWriteBackPort>(port ?? fake);
        services.AddSingleton<IClock>(new FakeClock(Ahora));
        services.AddOptions<AwWriteBackOptions>();
        return (services.BuildServiceProvider(), fake);
    }

    private static WriteBackResultadoWorker CrearWorker(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<IOptionsMonitor<AwWriteBackOptions>>(),
        NullLogger<WriteBackResultadoWorker>.Instance);

    [Fact]
    public async Task Tick_entrega_pendiente_por_pedido_y_confirma()
    {
        var dbName = Guid.NewGuid().ToString();
        var (sp, port) = ArmarWorkerServices(dbName);

        Guid pedidoId;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = Control();
            pedidoId = control.PedidoFacturableId!.Value;
            control.SolicitarWriteBackEstado("Facturado", "UUID-1", Ahora);
            db.IngestaControles.Add(control);
            await db.SaveChangesAsync();
        }

        var entregados = await CrearWorker(sp).TickAsync(CancellationToken.None);

        entregados.Should().Be(1);
        port.Ultimo.Should().NotBeNull();
        port.Ultimo!.SolicitudId.Should().Be(Guid.Empty); // por-pedido
        port.Ultimo.NumeroPedido.Should().Be("10432218");
        port.Ultimo.ErpPedidoId.Should().Be(pedidoId);
        port.Ultimo.EstadoFacturacion.Should().Be("Facturado");
        port.Ultimo.Uuid.Should().Be("UUID-1");

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = await db.IngestaControles.SingleAsync();
            control.WriteBackPendiente.Should().BeFalse();
            control.WriteBackAt.Should().Be(Ahora);
        }
    }

    [Fact]
    public async Task Tick_con_canal_caido_incrementa_intentos_y_sigue_pendiente()
    {
        var dbName = Guid.NewGuid().ToString();
        var throwing = new ThrowingWriteBackPort();
        var (sp, _) = ArmarWorkerServices(dbName, throwing);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = Control();
            control.SolicitarWriteBackEstado("Facturado", "UUID-1", Ahora);
            db.IngestaControles.Add(control);
            await db.SaveChangesAsync();
        }

        var entregados = await CrearWorker(sp).TickAsync(CancellationToken.None);

        entregados.Should().Be(0);
        throwing.Llamadas.Should().Be(1);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = await db.IngestaControles.SingleAsync();
            control.WriteBackPendiente.Should().BeTrue();
            control.WriteBackIntentos.Should().Be(1);
            control.WriteBackUltimoError.Should().Be("HC caída");
        }
    }

    [Fact]
    public async Task Tick_no_barre_pendientes_que_agotaron_MaxIntentos()
    {
        var dbName = Guid.NewGuid().ToString();
        var throwing = new ThrowingWriteBackPort();
        var (sp, _) = ArmarWorkerServices(dbName, throwing);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = Control();
            control.SolicitarWriteBackEstado("Facturado", "UUID-1", Ahora);
            for (var i = 0; i < 10; i++) // MaxIntentos default = 10
                control.RegistrarFalloWriteBack("HC caída", Ahora);
            db.IngestaControles.Add(control);
            await db.SaveChangesAsync();
        }

        var entregados = await CrearWorker(sp).TickAsync(CancellationToken.None);

        entregados.Should().Be(0);
        throwing.Llamadas.Should().Be(0); // agotado → no se martilla el canal
    }

    [Fact]
    public async Task Tick_reintento_por_solicitud_lleva_solicitudId_y_resultado()
    {
        var dbName = Guid.NewGuid().ToString();
        var (sp, port) = ArmarWorkerServices(dbName);
        var solicitudId = Guid.NewGuid();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = Control();
            control.SolicitarReintentoWriteBack(
                solicitudId, ResultadoSolicitudAw.Pospuesta, "pedido bloqueado", "SinFacturar", Ahora);
            db.IngestaControles.Add(control);
            await db.SaveChangesAsync();
        }

        var entregados = await CrearWorker(sp).TickAsync(CancellationToken.None);

        entregados.Should().Be(1);
        port.Ultimo!.SolicitudId.Should().Be(solicitudId);
        port.Ultimo.Resultado.Should().Be(ResultadoSolicitudAw.Pospuesta);
        port.Ultimo.Motivo.Should().Be("pedido bloqueado");
        port.Ultimo.EstadoFacturacion.Should().Be("SinFacturar");
    }
}
