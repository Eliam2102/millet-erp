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
using Millet.SharedKernel.Infrastructure;

namespace Millet.Facturacion.UnitTests.Ingesta;

/// <summary>
/// PR5 (ADR-0048 D3): reintento del write-back por solicitud hacia la tabla-puente
/// (TR-01 retiró el write-back por pedido) — semántica de dominio en <see cref="IngestaControl"/> y drenado por
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
        control.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora);

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
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
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
    public async Task Tick_con_canal_caido_incrementa_intentos_y_sigue_pendiente()
    {
        var dbName = Guid.NewGuid().ToString();
        var throwing = new ThrowingWriteBackPort();
        var (sp, _) = ArmarWorkerServices(dbName, throwing);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = Control();
            control.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora);
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
            control.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora);
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

    // ===== I13.6 · espera creciente =====

    [Fact]
    public async Task Tick_respeta_la_espera_creciente_desde_la_ultima_falla()
    {
        var (sp, port) = ArmarWorkerServices(Guid.NewGuid().ToString());
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            // 3 fallas: la siguiente espera es 2 min × 2² = 8 min.
            var reciente = Control();
            reciente.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora);
            for (var i = 0; i < 3; i++) reciente.RegistrarFalloWriteBack("HC caída", Ahora.AddMinutes(-5));
            var vencido = IngestaControl.Crear(EmpresaId, OrigenPedido.Aw, "10432219", "hash", 1, EstadoIngesta.Importado, Guid.NewGuid(), Ahora);
            vencido.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora);
            for (var i = 0; i < 3; i++) vencido.RegistrarFalloWriteBack("HC caída", Ahora.AddMinutes(-9));
            db.IngestaControles.AddRange(reciente, vencido);
            await db.SaveChangesAsync();
        }

        var entregados = await CrearWorker(sp).TickAsync(CancellationToken.None);

        entregados.Should().Be(1);
        port.Ultimo!.NumeroPedido.Should().Be("10432219");
    }

    [Fact]
    public async Task CA13_6_con_AW_caida_una_hora_el_pendiente_se_entrega_solo_y_una_vez()
    {
        var reloj = new RelojMovible(Ahora);
        var canal = new CanalIntermitente();
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentEmpresaContext>(new FakeEmpresaContext(EmpresaId));
        services.AddSingleton<IAuditOriginContext, AuditOriginContext>();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<FacturacionDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IAwWriteBackPort>(canal);
        services.AddSingleton<IClock>(reloj);
        services.AddOptions<AwWriteBackOptions>();
        using var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var control = Control();
            control.SolicitarReintentoWriteBack(Guid.NewGuid(), ResultadoSolicitudAw.Aplicada, null, "SinFacturar", Ahora);
            db.IngestaControles.Add(control);
            await db.SaveChangesAsync();
        }
        var worker = CrearWorker(sp);

        // Un tick cada 2 min (IntervalSeconds por omisión) durante 3 horas; A+W vuelve a la hora.
        for (var minuto = 0; minuto <= 180; minuto += 2)
        {
            reloj.UtcNow = Ahora.AddMinutes(minuto);
            canal.Caido = minuto < 60;
            await worker.TickAsync(CancellationToken.None);
        }

        using (var scope = sp.CreateScope())
        {
            var control = await scope.ServiceProvider.GetRequiredService<FacturacionDbContext>().IngestaControles.SingleAsync();
            control.WriteBackPendiente.Should().BeFalse("se entregó solo al volver A+W, sin reintento manual");
            control.WriteBackIntentos.Should().BeLessThan(10);
        }
        canal.Entregas.Should().Be(1, "sin duplicados");
        canal.Fallas.Should().BeLessThan(10, "la espera creciente no martilla el canal caído");
    }

    private sealed class RelojMovible(DateTimeOffset inicio) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = inicio;
    }

    private sealed class CanalIntermitente : IAwWriteBackPort
    {
        public bool Caido { get; set; }
        public int Fallas { get; private set; }
        public int Entregas { get; private set; }
        public Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken ct)
        {
            if (Caido) { Fallas++; throw new InvalidOperationException("HC caída"); }
            Entregas++;
            return Task.CompletedTask;
        }
    }
}
