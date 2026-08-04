using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

public sealed class DecidirLiberacionHandlerTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 14, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid UsuarioId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();

    // --------------------------------------------------- Cascada 1: serie

    [Fact]
    public async Task Serie_5000_siempre_libera_sin_evaluar_credito()
    {
        using var db = CrearDbContext();
        // Sin línea de crédito: si evaluara crédito, retendría.

        var (response, _) = await Handle(db, Comando(pedidoRef: "5000123", monto: 999_999m));

        response.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Serie);
        response.CreditoDisponibleSnapshot.Should().Be(0m); // snapshot SIEMPRE se guarda
    }

    [Fact]
    public async Task Serie_3000_nunca_libera_sin_override()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 1_000_000m); // crédito de sobra — la serie manda

        var (response, _) = await Handle(db, Comando(pedidoRef: "3000456", monto: 100m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Serie);
    }

    // --------------------------------------------------- Cascada 2: crédito

    [Fact]
    public async Task Credito_suficiente_libera()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);

        var (response, _) = await Handle(db, Comando(pedidoRef: "9000001", monto: 60_000m));

        response.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        response.CreditoDisponibleSnapshot.Should().Be(100_000m);
    }

    [Fact]
    public async Task Credito_insuficiente_por_facturado_retiene()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        await AgregarFacturaAbierta(db, total: 70_000m);

        var (response, _) = await Handle(db, Comando(pedidoRef: "9000002", monto: 50_000m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        response.CreditoDisponibleSnapshot.Should().Be(30_000m);
    }

    [Fact]
    public async Task Sin_linea_activa_retiene()
    {
        using var db = CrearDbContext();

        var (response, _) = await Handle(db, Comando(pedidoRef: "9000003", monto: 10m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
    }

    [Fact]
    public async Task Linea_bloqueada_retiene()
    {
        using var db = CrearDbContext();
        var linea = await AgregarLinea(db, limite: 100_000m);
        linea.Bloquear("Cartera vencida");
        await db.SaveChangesAsync();

        var (response, _) = await Handle(db, Comando(pedidoRef: "9000004", monto: 10m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.CreditoDisponibleSnapshot.Should().Be(0m);
    }

    // --------------------------------------------------- Cascada 3: override

    [Fact]
    public async Task Override_libera_serie_bloqueada_y_se_consume_en_la_misma_tx()
    {
        using var db = CrearDbContext();
        var autorizacion = await AgregarAutorizacion(db, beneficiario: UsuarioId);

        var (response, _) = await Handle(db, Comando(
            pedidoRef: "3000789", monto: 50_000m, overrideId: autorizacion.Id));

        response.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Override);
        response.OverrideId.Should().Be(autorizacion.Id);

        var consumida = await db.AutorizacionesCredito.SingleAsync();
        consumida.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
        consumida.DecisionLiberacionId.Should().Be(response.Id);
    }

    [Fact]
    public async Task Override_de_otro_beneficiario_falla_y_no_persiste_decision()
    {
        using var db = CrearDbContext();
        var ajena = await AgregarAutorizacion(db, beneficiario: Guid.NewGuid());

        var act = () => Handle(db, Comando(pedidoRef: "3000790", monto: 10m, overrideId: ajena.Id));

        await act.Should().ThrowAsync<BusinessRuleException>()
            .Where(e => e.Code == "AC_BENEFICIARIO_DISTINTO");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Override_no_se_consume_si_el_credito_alcanza()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        var autorizacion = await AgregarAutorizacion(db, beneficiario: UsuarioId);

        var (response, _) = await Handle(db, Comando(
            pedidoRef: "9000005", monto: 10_000m, overrideId: autorizacion.Id));

        response.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        response.OverrideId.Should().BeNull();
        (await db.AutorizacionesCredito.SingleAsync()).Estado
            .Should().Be(EstadoAutorizacionCredito.Autorizada);
    }

    // --------------------------------------------------- Evento

    [Fact]
    public async Task Decidir_publica_DecisionLiberacionEmitidaEvent()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);

        var (_, publicados) = await Handle(db, Comando(pedidoRef: "9000006", monto: 5_000m));

        publicados.Should().ContainSingle()
            .Which.Should().BeOfType<Application.Integration.DecisionLiberacionEmitidaEvent>()
            .Which.Resultado.Should().Be("Liberado");
    }

    // --------------------------------------------------- helpers

    private static DecidirLiberacionCommand Comando(string pedidoRef, decimal monto, Guid? overrideId = null) =>
        new(pedidoRef, ClienteId, "MXN", monto, overrideId);

    private static async Task<(DecisionLiberacionResponse Response, List<object> Publicados)> Handle(
        CuentasPorCobrarDbContext db, DecidirLiberacionCommand command)
    {
        var publisher = new FakePublisher();
        var handler = new DecidirLiberacionHandler(
            db, publisher, new FakeEmpresaContext(EmpresaId), new FakeUserContext(UsuarioId), new FakeClock());
        var response = await handler.Handle(command, CancellationToken.None);
        return (response, publisher.Publicados);
    }

    private static async Task<LineaCreditoAggregate> AgregarLinea(CuentasPorCobrarDbContext db, decimal limite)
    {
        var linea = LineaCreditoAggregate.Crear(
            EmpresaId, ClienteId, "MXN", limite, OrigenLineaCredito.Solunion, plazoDias: 45);
        db.LineasCredito.Add(linea);
        await db.SaveChangesAsync();
        return linea;
    }

    private static async Task AgregarFacturaAbierta(CuentasPorCobrarDbContext db, decimal total)
    {
        db.FacturasCartera.Add(FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente", Guid.NewGuid().ToString(),
            "FV-1", total, "MXN", "PPD", Ahora, Ahora.AddDays(45)));
        await db.SaveChangesAsync();
    }

    private static async Task<AutorizacionCredito> AgregarAutorizacion(
        CuentasPorCobrarDbContext db, Guid beneficiario)
    {
        var autorizacion = AutorizacionCredito.Crear(
            EmpresaId, Guid.NewGuid(), beneficiario,
            "Cliente estratégico", "3000789", Ahora, TimeSpan.FromHours(24));
        db.AutorizacionesCredito.Add(autorizacion);
        await db.SaveChangesAsync();
        return autorizacion;
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_liberacion_{Guid.NewGuid()}")
            .Options;
        var db = new CuentasPorCobrarDbContext(options, new FakeEmpresaContext(EmpresaId));

        // Seed del catálogo de series (mismas filas que el HasData de la
        // configuración — InMemory sin EnsureCreated no aplica HasData).
        db.ReglasLiberacionSerie.AddRange(
            ReglaLiberacionSerieConfiguration.SeedReglas.Select(r =>
                new ReglaLiberacionSerie(r.Id, r.Prefijo, r.Comportamiento)));
        db.SaveChanges();
        return db;
    }

    private sealed class FakePublisher : IIntegrationEventPublisher
    {
        public List<object> Publicados { get; } = [];
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken)
        {
            Publicados.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUserContext(Guid userId) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? UserName => "test";
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
