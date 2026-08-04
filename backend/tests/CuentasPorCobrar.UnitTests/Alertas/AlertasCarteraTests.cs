using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Domain.Alertas;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Alertas;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

public sealed class AlertaCarteraAggregateTests
{
    [Fact]
    public void Atender_una_sola_vez()
    {
        var a = new AlertaCartera(
            Guid.NewGuid(), Guid.NewGuid(), TipoAlertaCartera.ExcesoCredito, "MXN",
            "Saldo excede límite", DateTimeOffset.UtcNow);

        a.Atender(Guid.NewGuid(), DateTimeOffset.UtcNow);
        a.Atendida.Should().BeTrue();

        var act = () => a.Atender(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AL_YA_ATENDIDA");
    }
}

public sealed class EvaluarAlertasCarteraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    [Fact]
    public async Task Solunion_90d_alerta_cartera_asegurada_vencida()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, OrigenLineaCredito.Solunion, limite: 1_000_000m);
        AgregarFactura(db, vencida: 95, total: 10_000m);
        await db.SaveChangesAsync();

        var creadas = await Evaluar(db);

        // 95 días vencidos dispara SOLUNION (≥90) Y el auto-bloqueo (≥90).
        creadas.Should().Be(2);
        (await db.AlertasCartera.Select(a => a.Tipo).ToListAsync())
            .Should().Contain(TipoAlertaCartera.Solunion90d);
    }

    [Fact]
    public async Task Solunion_no_alerta_cartera_interna()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, OrigenLineaCredito.Interno, limite: 1_000_000m);
        AgregarFactura(db, vencida: 95, total: 10_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        (await db.AlertasCartera.Where(a => a.Tipo == TipoAlertaCartera.Solunion90d).CountAsync())
            .Should().Be(0);
    }

    [Fact]
    public async Task Exceso_de_credito_alerta_cuando_saldo_supera_limite()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, OrigenLineaCredito.Interno, limite: 5_000m);
        AgregarFactura(db, vencida: 5, total: 8_000m);
        await db.SaveChangesAsync();

        var creadas = await Evaluar(db);

        creadas.Should().Be(1);
        (await db.AlertasCartera.SingleAsync()).Tipo.Should().Be(TipoAlertaCartera.ExcesoCredito);
    }

    [Fact]
    public async Task Auto_bloqueo_bloquea_la_linea_con_motivo_automatico()
    {
        using var db = CrearDbContext();
        var linea = AgregarLinea(db, OrigenLineaCredito.Interno, limite: 1_000_000m);
        AgregarFactura(db, vencida: 120, total: 1_000m);
        await db.SaveChangesAsync();

        var creadas = await Evaluar(db);

        creadas.Should().Be(1);
        var bloqueada = await db.LineasCredito.SingleAsync(l => l.Id == linea.Id);
        bloqueada.Estado.Should().Be(EstadoLineaCredito.Bloqueada);
        bloqueada.MotivoBloqueo.Should().StartWith("Auto-bloqueo");
        (await db.AlertasCartera.SingleAsync()).Tipo.Should().Be(TipoAlertaCartera.AutoBloqueoVencimiento);
    }

    [Fact]
    public async Task Re_evaluacion_no_duplica_alertas_sin_atender()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, OrigenLineaCredito.Interno, limite: 5_000m);
        AgregarFactura(db, vencida: 5, total: 8_000m);
        await db.SaveChangesAsync();

        (await Evaluar(db)).Should().Be(1);
        (await Evaluar(db)).Should().Be(0);
        (await db.AlertasCartera.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Publica_AlertaCarteraGeneradaEvent_por_alerta()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, OrigenLineaCredito.Interno, limite: 5_000m);
        AgregarFactura(db, vencida: 5, total: 8_000m);
        await db.SaveChangesAsync();

        var publisher = new FakePublisher();
        await Evaluar(db, publisher);

        publisher.Publicados.Should().ContainSingle()
            .Which.Should().BeOfType<AlertaCarteraGeneradaEvent>()
            .Which.Tipo.Should().Be("ExcesoCredito");
    }

    // --------------------------------------------------- helpers

    private static async Task<int> Evaluar(CuentasPorCobrarDbContext db, FakePublisher? publisher = null)
    {
        var handler = new EvaluarAlertasCarteraHandler(
            db,
            publisher ?? new FakePublisher(),
            Options.Create(new AlertasCarteraOptions()),
            new FakeClock(),
            NullLogger<EvaluarAlertasCarteraHandler>.Instance);
        return await handler.Handle(new EvaluarAlertasCarteraCommand(), CancellationToken.None);
    }

    private static LineaCreditoAggregate AgregarLinea(
        CuentasPorCobrarDbContext db, OrigenLineaCredito origen, decimal limite)
    {
        var linea = LineaCreditoAggregate.Crear(EmpresaId, ClienteId, "MXN", limite, origen, 45);
        db.LineasCredito.Add(linea);
        return linea;
    }

    private static void AgregarFactura(CuentasPorCobrarDbContext db, int vencida, decimal total)
    {
        var vencimiento = Ahora.AddDays(-vencida);
        db.FacturasCartera.Add(FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            Guid.NewGuid().ToString(), "FV-1", total, "MXN", "PPD",
            fechaTimbrado: vencimiento.AddDays(-45), fechaVencimiento: vencimiento));
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_alertas_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken ct)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
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
