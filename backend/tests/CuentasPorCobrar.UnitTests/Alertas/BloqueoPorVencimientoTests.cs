using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Alertas;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// C7.5 · Bloqueo automático por vencimiento: plazo del cliente + 15 días de
/// tolerancia (R7/P7.2), desbloqueo al pagar (C7.5-a) y alerta del seguro
/// antes de los 90 días (CA7.11).
/// </summary>
public sealed class BloqueoPorVencimientoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    [Fact]
    public async Task CA7_12_cliente_con_31_dias_en_plazo_de_15_mas_15_de_tolerancia_se_bloquea()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        var l = await db.LineasCredito.SingleAsync(x => x.Id == linea.Id);
        l.Estado.Should().Be(EstadoLineaCredito.Bloqueada);
        l.BloqueoAutomatico.Should().BeTrue();
        l.MotivoBloqueo.Should().Contain("plazo de 15 + 15 de tolerancia");
        (await db.AlertasCartera.SingleAsync()).Tipo.Should().Be(TipoAlertaCartera.AutoBloqueoVencimiento);
    }

    [Fact]
    public async Task CA7_12_con_30_dias_sigue_dentro_de_la_tolerancia_y_no_se_bloquea()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        AgregarFactura(db, diasDesdeTimbrado: 30, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);
        (await db.AlertasCartera.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task El_umbral_ya_no_es_90_dias_fijos_sino_el_plazo_del_cliente_mas_la_tolerancia()
    {
        using var db = CrearDbContext();
        // Plazo 60 (Canadá): 76 días desde el timbrado = 16 de vencida, muy lejos de 90.
        var linea = AgregarLinea(db, plazoDias: 60);
        AgregarFactura(db, diasDesdeTimbrado: 76, plazoDias: 60, total: 10_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);
    }

    [Fact]
    public async Task La_tolerancia_es_configurable()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();

        await Evaluar(db, new AlertasCarteraOptions { ToleranciaDiasVencido = 20 });

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);
    }

    [Fact]
    public async Task CA7_11_factura_asegurada_con_85_dias_de_vencida_alerta_antes_de_los_90()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, plazoDias: 45, origen: OrigenLineaCredito.Solunion);
        AgregarFactura(db, diasDesdeTimbrado: 45 + 85, plazoDias: 45, total: 10_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        var alerta = await db.AlertasCartera.SingleAsync(a => a.Tipo == TipoAlertaCartera.Solunion90d);
        alerta.Detalle.Should().Contain("faltan 5 día(s)");
    }

    [Fact]
    public async Task C7_5_a_cliente_bloqueado_que_paga_todo_lo_vencido_se_desbloquea_solo()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        var factura = AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();
        await Evaluar(db);
        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);

        await Cobrar(db, factura, 10_000m);

        var l = await db.LineasCredito.SingleAsync(x => x.Id == linea.Id);
        l.Estado.Should().Be(EstadoLineaCredito.Activa);
        l.BloqueoAutomatico.Should().BeFalse();
        l.MotivoBloqueo.Should().BeNull();
    }

    [Fact]
    public async Task Un_pago_parcial_no_desbloquea()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        var factura = AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();
        await Evaluar(db);

        await Cobrar(db, factura, 4_000m);

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);
    }

    [Fact]
    public async Task Pagar_lo_vencido_desbloquea_aunque_quede_una_factura_dentro_de_la_tolerancia()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        var vencida = AgregarFactura(db, diasDesdeTimbrado: 40, plazoDias: 15, total: 10_000m);
        AgregarFactura(db, diasDesdeTimbrado: 20, plazoDias: 15, total: 5_000m);
        await db.SaveChangesAsync();
        await Evaluar(db);

        await Cobrar(db, vencida, 10_000m);

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);
    }

    [Fact]
    public async Task Un_bloqueo_manual_no_se_levanta_al_pagar()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        linea.Bloquear("Decisión de Crédito y Cobranza");
        var factura = AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();

        await Cobrar(db, factura, 10_000m);
        await Evaluar(db);

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);
    }

    [Fact]
    public async Task El_desbloqueo_manual_cubre_lo_vencido_y_otra_factura_que_rebase_despues_vuelve_a_bloquear()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        linea.BloquearPorVencimiento("Bloqueo automático");
        linea.Desbloquear("Acuerdo de pago autorizado", Ahora.AddDays(-5));
        AgregarFactura(db, diasDesdeTimbrado: 50, plazoDias: 15, total: 10_000m);
        await db.SaveChangesAsync();

        // Lo vencido al desbloquear quedó cubierto.
        await Evaluar(db);
        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);

        // Una factura que rebasa plazo + tolerancia hoy (después del desbloqueo) vuelve a bloquear.
        AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15, total: 2_000m);
        await db.SaveChangesAsync();
        await Evaluar(db);
        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);
    }

    [Fact]
    public async Task La_evaluacion_desbloquea_si_ya_no_hay_facturas_fuera_de_plazo()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, plazoDias: 15);
        linea.BloquearPorVencimiento("Bloqueo automático");
        AgregarFactura(db, diasDesdeTimbrado: 10, plazoDias: 15, total: 1_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        (await db.LineasCredito.SingleAsync(x => x.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);
    }

    // --------------------------------------------------- helpers

    private static async Task Evaluar(CuentasPorCobrarDbContext db, AlertasCarteraOptions? options = null)
    {
        var handler = new EvaluarAlertasCarteraHandler(
            db, new FakePublisher(), Options.Create(options ?? new AlertasCarteraOptions()),
            new FakeClock(), NullLogger<EvaluarAlertasCarteraHandler>.Instance);
        await handler.Handle(new EvaluarAlertasCarteraCommand(), CancellationToken.None);
    }

    private static async Task Cobrar(CuentasPorCobrarDbContext db, FacturaCartera factura, decimal importe)
    {
        var handler = new CobroMostradorRegistradoHandler(
            db, new FakeClock(), NullLogger<CobroMostradorRegistradoHandler>.Instance,
            Options.Create(new AlertasCarteraOptions()));
        await handler.Handle(new CobroMostradorRegistradoCommand(Guid.NewGuid(), new CobroMostradorRegistradoPayload(
            EmpresaId, Ahora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            factura.FacturaVentaId, "Ingreso", "Pedido", importe)),
            CancellationToken.None);
    }

    private static LineaCreditoAggregate AgregarLinea(
        CuentasPorCobrarDbContext db, int plazoDias, OrigenLineaCredito origen = OrigenLineaCredito.Interno)
    {
        var linea = LineaCreditoAggregate.Crear(EmpresaId, ClienteId, "MXN", 1_000_000m, origen, plazoDias);
        db.LineasCredito.Add(linea);
        return linea;
    }

    private static FacturaCartera AgregarFactura(
        CuentasPorCobrarDbContext db, int diasDesdeTimbrado, int plazoDias, decimal total)
    {
        var timbrado = Ahora.AddDays(-diasDesdeTimbrado);
        var factura = FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente DEMO",
            Guid.NewGuid().ToString(), "FV-DEMO", total, "MXN", "PPD",
            fechaTimbrado: timbrado, fechaVencimiento: timbrado.AddDays(plazoDias));
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_c75_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
