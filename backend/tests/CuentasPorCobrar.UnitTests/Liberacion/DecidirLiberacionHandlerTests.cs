using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.UnitTests.CreditoDisponible;
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
    public async Task Serie_7800_nunca_libera_sin_override()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 1_000_000m); // crédito de sobra — la serie manda

        var (response, _) = await Handle(db, Comando(pedidoRef: "7800456", monto: 100m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Serie);
    }

    // --------------------------------------------------- Cascada 2: crédito

    [Fact]
    public async Task Credito_suficiente_libera()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000001", monto: 60_000m));

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

        // C7.3: 30,000 / 70,000 = 42 % (< 50 %) → retenido. Con 50,000 ahora
        // sería 60 % → PendienteAutorizacion (CoberturaLiberacionHandlerTests).
        var (response, _) = await Handle(db, Comando(pedidoRef: "6000002", monto: 70_000m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
        response.CreditoDisponibleSnapshot.Should().Be(30_000m);
    }

    [Fact]
    public async Task Sin_linea_activa_retiene()
    {
        using var db = CrearDbContext();

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000003", monto: 10m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
    }

    [Fact]
    public async Task Linea_bloqueada_retiene()
    {
        using var db = CrearDbContext();
        var linea = await AgregarLinea(db, limite: 100_000m);
        linea.Bloquear("Cartera vencida");
        await db.SaveChangesAsync();

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000004", monto: 10m));

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
            pedidoRef: "7800789", monto: 50_000m, overrideId: autorizacion.Id));

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

        var act = () => Handle(db, Comando(pedidoRef: "7800790", monto: 10m, overrideId: ajena.Id));

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
            pedidoRef: "6000005", monto: 10_000m, overrideId: autorizacion.Id));

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

        var (_, publicados) = await Handle(db, Comando(pedidoRef: "6000006", monto: 5_000m));

        publicados.Should().ContainSingle()
            .Which.Should().BeOfType<Application.Integration.DecisionLiberacionEmitidaEvent>()
            .Which.Resultado.Should().Be("Liberado");
    }

    // --------------------------------------------------- C7.2: series reales de Millet

    [Theory]
    // R1: la serie son los 2 primeros dígitos del pedido (folios de A+W de 8 dígitos).
    [InlineData("50")]
    [InlineData("55")]
    [InlineData("56")]
    [InlineData("70")]
    [InlineData("72")]
    [InlineData("75")]
    public async Task CA7_4_serie_que_siempre_libera_libera_sin_credito(string serie)
    {
        using var db = CrearDbContext();
        // Cliente sin línea de crédito: por crédito quedaría retenido.

        var (response, _) = await Handle(db, Comando(pedidoRef: serie + "047568", monto: 250_000m));

        response.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Serie);
    }

    [Theory]
    [InlineData("58")]
    [InlineData("59")]
    [InlineData("78")]
    [InlineData("79")]
    [InlineData("90")]
    public async Task CA7_5_serie_que_nunca_libera_retiene_aunque_alcance_el_credito(string serie)
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 1_000_000m);

        var (response, _) = await Handle(db, Comando(pedidoRef: serie + "047568", monto: 10m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Serie);
        response.CreditoDisponibleSnapshot.Should().Be(1_000_000m);
    }

    [Fact]
    public async Task CA7_5_serie_7800_sale_solo_con_autorizacion()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 1_000_000m);
        var autorizacion = await AgregarAutorizacion(db, beneficiario: UsuarioId);

        var (response, _) = await Handle(db, Comando(
            pedidoRef: "7800789", monto: 10m, overrideId: autorizacion.Id));

        response.Resultado.Should().Be(ResultadoLiberacion.LiberadoConOverride);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Override);
    }

    [Fact]
    public async Task Serie_no_registrada_evalua_credito()
    {
        // Propuesta C7.2: las series de exportación que el cuestionario no
        // menciona evalúan crédito (no hay regla para ellas).
        using var db = CrearDbContext();

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000001", monto: 10m));

        response.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
    }

    [Fact]
    public async Task C7_2_a_cambio_de_regla_aplica_a_la_siguiente_evaluacion()
    {
        using var db = CrearDbContext();
        var (antes, _) = await Handle(db, Comando(pedidoRef: "5500001", monto: 10m));
        antes.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Serie);

        var regla = await db.ReglasLiberacionSerie.SingleAsync(r => r.Prefijo == "55");
        await new CambiarComportamientoSerieHandler(db).Handle(
            new CambiarComportamientoSerieCommand(regla.Id, regla.Version, ComportamientoSerie.EvaluaCredito),
            CancellationToken.None);

        var (despues, _) = await Handle(db, Comando(pedidoRef: "5500002", monto: 10m));
        despues.Resultado.Should().Be(ResultadoLiberacion.Retenido);
        despues.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
    }

    [Fact]
    public async Task Regla_desactivada_deja_que_la_serie_evalue_credito()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        var regla = await db.ReglasLiberacionSerie.SingleAsync(r => r.Prefijo == "90");
        await new CambiarEstadoReglaSerieHandler(db).Handle(
            new CambiarEstadoReglaSerieCommand(regla.Id, regla.Version, Activo: false), CancellationToken.None);

        var (response, _) = await Handle(db, Comando(pedidoRef: "9000001", monto: 10m));

        response.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
    }

    // --------------------------------------------------- C7.1: liberado sin facturar

    [Fact]
    public async Task CA7_1_liberado_sin_facturar_resta_del_disponible_y_el_pedido_se_libera()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        await AgregarFacturaAbierta(db, total: 40_000m);
        var pedidos = new PedidosSinFacturarFalso().Agregar(ClienteId, "6000100", 20_000m);

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000200", monto: 30_000m), pedidos);

        response.CreditoDisponibleSnapshot.Should().Be(40_000m);
        response.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        response.ReglaAplicada.Should().Be(ReglaAplicadaLiberacion.Credito);
    }

    [Fact]
    public async Task C7_1_el_pedido_evaluado_no_compromete_su_propio_credito()
    {
        // El pedido que se evalúa ya entró en firme a Facturación: no se resta dos veces.
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(ClienteId, "6000300", 30_000m)
            .Agregar(ClienteId, "6000100", 20_000m);

        var (response, _) = await Handle(db, Comando(pedidoRef: " 6000300 ", monto: 30_000m), pedidos);

        response.CreditoDisponibleSnapshot.Should().Be(80_000m);
    }

    [Fact]
    public async Task C7_1_pedido_cubierto_por_anticipo_no_compromete_credito()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(ClienteId, "6000100", 20_000m, anticipoLigado: 20_000m)
            .Agregar(ClienteId, "6000101", 10_000m, anticipoLigado: 15_000m); // sobra anticipo: no suma negativo

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000200", monto: 30_000m), pedidos);

        response.CreditoDisponibleSnapshot.Should().Be(100_000m);
    }

    [Fact]
    public async Task C7_1_pedido_en_otra_moneda_no_resta_de_la_linea()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        var pedidos = new PedidosSinFacturarFalso().Agregar(ClienteId, "6000100", 20_000m, moneda: "USD");

        var (response, _) = await Handle(db, Comando(pedidoRef: "6000200", monto: 30_000m), pedidos);

        response.CreditoDisponibleSnapshot.Should().Be(100_000m);
    }

    [Fact]
    public async Task C7_1_consulta_y_liberacion_dan_el_mismo_disponible()
    {
        using var db = CrearDbContext();
        await AgregarLinea(db, limite: 100_000m);
        await AgregarFacturaAbierta(db, total: 40_000m);
        var pedidos = new PedidosSinFacturarFalso()
            .Agregar(ClienteId, "6000100", 20_000m)
            .Agregar(ClienteId, "6000101", 8_000m, anticipoLigado: 3_000m);

        var consulta = await new Millet.CuentasPorCobrar.Application.CreditoDisponible.CreditoDisponibleHandler(db, pedidos)
            .Handle(new Millet.CuentasPorCobrar.Application.CreditoDisponible.CreditoDisponibleQuery(ClienteId), CancellationToken.None);
        var (decision, _) = await Handle(db, Comando(pedidoRef: "6000200", monto: 1_000m), pedidos);

        var linea = consulta.Lineas.Single();
        linea.LiberadoSinFactura.Should().Be(25_000m);
        linea.Disponible.Should().Be(35_000m);
        decision.CreditoDisponibleSnapshot.Should().Be(linea.Disponible);
    }

    // --------------------------------------------------- helpers

    private static DecidirLiberacionCommand Comando(string pedidoRef, decimal monto, Guid? overrideId = null) =>
        new(pedidoRef, ClienteId, "MXN", monto, overrideId);

    private static async Task<(DecisionLiberacionResponse Response, List<object> Publicados)> Handle(
        CuentasPorCobrarDbContext db, DecidirLiberacionCommand command, PedidosSinFacturarFalso? pedidos = null)
    {
        var publisher = new FakePublisher();
        var handler = new DecidirLiberacionHandler(
            db, publisher, new FakeEmpresaContext(EmpresaId), new FakeUserContext(UsuarioId), new FakeClock(),
            pedidos ?? new PedidosSinFacturarFalso());
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
            "Cliente estratégico", "7800789", Ahora, TimeSpan.FromHours(24));
        db.AutorizacionesCredito.Add(autorizacion);
        await db.SaveChangesAsync();
        return autorizacion;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Autorizacion_solo_se_crea_para_beneficiario_con_permiso(bool permitido)
    {
        await using var db = CrearDbContext();
        var handler = new Millet.CuentasPorCobrar.Application.Autorizaciones.CrearAutorizacionCreditoHandler(
            db, new FakeEmpresaContext(EmpresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(), new Beneficiarios(permitido));
        var command = new Millet.CuentasPorCobrar.Application.Autorizaciones.CrearAutorizacionCreditoCommand(Guid.NewGuid(), "Pago en tránsito", "FIX-PRUB", 24);
        if (permitido)
        {
            var res = await handler.Handle(command, default);
            res.BeneficiarioUsuarioId.Should().Be(command.BeneficiarioUsuarioId);
            db.AutorizacionesCredito.Should().ContainSingle();
        }
        else
        {
            var act = () => handler.Handle(command, default);
            await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_BENEFICIARIO_SIN_PERMISO");
            db.AutorizacionesCredito.Should().BeEmpty();
        }
    }
    private sealed class Beneficiarios(bool permitir) : Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros.IBeneficiarioCreditoReadPort
    {
        public Task<bool> PuedeDecidirAsync(Guid usuario, Guid empresa, CancellationToken ct) => Task.FromResult(permitir);
        public Task<IReadOnlyList<Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros.BeneficiarioCreditoDto>> ListarAsync(Guid empresa, CancellationToken ct) => Task.FromResult<IReadOnlyList<Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros.BeneficiarioCreditoDto>>([]);
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_liberacion_{Guid.NewGuid()}")
            .Options;
        var db = new CuentasPorCobrarDbContext(options, new FakeEmpresaContext(EmpresaId));

        // Catálogo de series de Millet (las mismas filas que carga la
        // migración ReglasSerieMillet).
        db.ReglasLiberacionSerie.AddRange(
            ReglaLiberacionSerieConfiguration.SeriesMillet.Select(r =>
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
