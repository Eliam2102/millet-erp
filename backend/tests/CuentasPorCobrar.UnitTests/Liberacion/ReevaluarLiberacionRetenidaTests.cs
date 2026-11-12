using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.Liberacion;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;
using Millet.CuentasPorCobrar.UnitTests.CreditoDisponible;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// M-238: corregir el dato que retuvo un pedido (cliente; saldo mal aplicado y condición se corrigen en cartera) y volver a
/// evaluar la liberación. La reevaluación es una decisión nueva con la misma cascada; el historial muestra las dos
/// evaluaciones ligadas por la corrección.
/// </summary>
public sealed class ReevaluarLiberacionRetenidaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Coordinador = Guid.NewGuid(); // registra la decisión
    private static readonly Guid Corrige = Guid.NewGuid();     // CxC que corrige y reevalúa
    private static readonly Guid ClienteA = Guid.NewGuid();    // cliente con el saldo mal aplicado / cliente equivocado
    private static readonly Guid ClienteB = Guid.NewGuid();    // cliente correcto
    private const string Pedido = "6024101";

    // ---------------------------------------------------------------- ficha: saldo mal aplicado

    [Fact]
    public async Task Un_pedido_retenido_por_saldo_mal_aplicado_se_libera_al_corregir_y_reevaluar_y_el_historial_muestra_ambas_evaluaciones()
    {
        await using var db = Db();
        var factura = await LineaConFacturaAbiertaAsync(db, ClienteA, limite: 100_000m, facturado: 100_000m);
        var retenida = await DecidirAsync(db, ClienteA, 50_000m);
        retenida.Resultado.Should().Be(ResultadoLiberacion.Retenido); // el cobro de 80,000 estaba aplicado en otro lado

        // Se corrige el saldo en cartera (el cobro se aplica a la factura correcta) y se reevalúa.
        factura.AplicarPago(80_000m);
        await db.SaveChangesAsync();
        var res = await ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.SaldoAplicado, "Cobro aplicado a la factura correcta");

        (res.Nueva.Resultado, res.Nueva.ReglaAplicada, res.Nueva.CreditoDisponibleSnapshot)
            .Should().Be((ResultadoLiberacion.Liberado, ReglaAplicadaLiberacion.Credito, 80_000m));
        (res.Anterior.Resultado, res.Anterior.CreditoDisponibleSnapshot).Should().Be((ResultadoLiberacion.Retenido, 0m));
        res.Anterior.ReevaluadaPorId.Should().Be(res.Nueva.Id);
        res.Nueva.ReevaluaDecisionId.Should().Be(retenida.Id);

        var historial = await new HistorialLiberacionPedidoHandler(db)
            .Handle(new HistorialLiberacionPedidoQuery(Pedido), default);
        historial.Select(h => h.Decision.Resultado).Should().Equal(ResultadoLiberacion.Retenido, ResultadoLiberacion.Liberado);
        historial[0].Correccion.Should().BeNull();
        (historial[1].Correccion!.Tipo, historial[1].Correccion!.Detalle, historial[1].Correccion!.DecisionRetenidaId)
            .Should().Be((TipoCorreccionLiberacion.SaldoAplicado, "Cobro aplicado a la factura correcta", retenida.Id));
    }

    [Fact]
    public async Task Si_el_saldo_sigue_igual_la_reevaluacion_vuelve_a_retener_y_la_cadena_se_puede_corregir_otra_vez()
    {
        await using var db = Db();
        var factura = await LineaConFacturaAbiertaAsync(db, ClienteA, limite: 100_000m, facturado: 100_000m);
        var primera = await DecidirAsync(db, ClienteA, 50_000m);

        var segunda = await ReevaluarAsync(db, primera.Id, TipoCorreccionLiberacion.Condicion, "Se revisó la condición del cliente");
        segunda.Nueva.Resultado.Should().Be(ResultadoLiberacion.Retenido);

        factura.AplicarPago(80_000m);
        await db.SaveChangesAsync();
        var tercera = await ReevaluarAsync(db, segunda.Nueva.Id, TipoCorreccionLiberacion.SaldoAplicado, "Cobro reaplicado");
        tercera.Nueva.Resultado.Should().Be(ResultadoLiberacion.Liberado);
        (await db.CorreccionesLiberacion.CountAsync()).Should().Be(2);
    }

    // ---------------------------------------------------------------- cliente equivocado

    [Fact]
    public async Task Un_pedido_retenido_por_cliente_equivocado_se_libera_al_corregir_el_cliente()
    {
        await using var db = Db();
        await LineaConFacturaAbiertaAsync(db, ClienteB, limite: 100_000m, facturado: 0m);
        var retenida = await DecidirAsync(db, ClienteA, 50_000m); // ClienteA no tiene línea de crédito activa
        retenida.Resultado.Should().Be(ResultadoLiberacion.Retenido);

        var res = await ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.Cliente, "El pedido era de otro cliente", ClienteB);

        (res.Nueva.ClienteId, res.Nueva.Resultado).Should().Be((ClienteB, ResultadoLiberacion.Liberado));
        res.Anterior.ClienteId.Should().Be(ClienteA);
    }

    // ---------------------------------------------------------------- guardas

    [Fact]
    public async Task Solo_se_reevalua_una_retencion_vigente_una_sola_vez()
    {
        await using var db = Db();
        await LineaConFacturaAbiertaAsync(db, ClienteA, limite: 100_000m, facturado: 0m);
        var liberada = await DecidirAsync(db, ClienteA, 10_000m);
        liberada.Resultado.Should().Be(ResultadoLiberacion.Liberado);

        var noRetenida = () => ReevaluarAsync(db, liberada.Id, TipoCorreccionLiberacion.SaldoAplicado, "No aplica");
        await noRetenida.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_NO_RETENIDA");

        await using var db2 = Db();
        await LineaConFacturaAbiertaAsync(db2, ClienteA, limite: 100_000m, facturado: 100_000m);
        var retenida = await DecidirAsync(db2, ClienteA, 50_000m);
        await ReevaluarAsync(db2, retenida.Id, TipoCorreccionLiberacion.Condicion, "Primera reevaluación");

        var otraVez = () => ReevaluarAsync(db2, retenida.Id, TipoCorreccionLiberacion.Condicion, "Segunda reevaluación");
        await otraVez.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "DL_YA_REEVALUADA");
    }

    [Fact]
    public async Task Una_retencion_superada_por_una_decision_mas_reciente_no_se_reevalua()
    {
        await using var db = Db();
        await LineaConFacturaAbiertaAsync(db, ClienteA, limite: 100_000m, facturado: 100_000m);
        var vieja = await DecidirAsync(db, ClienteA, 50_000m, ahora: Ahora);
        await DecidirAsync(db, ClienteA, 50_000m, ahora: Ahora.AddMinutes(5)); // otra decisión posterior del mismo pedido

        var act = () => ReevaluarAsync(db, vieja.Id, TipoCorreccionLiberacion.SaldoAplicado, "Cobro corregido");

        await act.Should().ThrowAsync<ConflictException>().Where(e => e.Code == "DL_RETENIDA_SUPERADA");
    }

    [Fact]
    public async Task Corregir_el_cliente_exige_otro_cliente_existente_y_los_demas_tipos_no_lo_aceptan()
    {
        await using var db = Db();
        await LineaConFacturaAbiertaAsync(db, ClienteA, limite: 100_000m, facturado: 100_000m);
        var retenida = await DecidirAsync(db, ClienteA, 50_000m);

        var sinCliente = () => ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.Cliente, "Cliente equivocado");
        await sinCliente.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_CLIENTE_REQUERIDO");

        var mismo = () => ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.Cliente, "Cliente equivocado", ClienteA);
        await mismo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_CLIENTE_SIN_CAMBIO");

        var inexistente = () => ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.Cliente, "Cliente equivocado", Guid.NewGuid(), clientesConocidos: [ClienteA]);
        await inexistente.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_CLIENTE_NO_ENCONTRADO");

        var sobrante = () => ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.SaldoAplicado, "Cobro corregido", ClienteB);
        await sobrante.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DL_CLIENTE_SOLO_EN_CORRECCION_DE_CLIENTE");

        (await db.CorreccionesLiberacion.CountAsync()).Should().Be(0, "ninguna corrección inválida deja rastro ni decisión nueva");
        (await db.DecisionesLiberacion.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void El_detalle_de_la_correccion_es_obligatorio_y_la_reevaluacion_distinta_de_la_retenida()
    {
        var retenida = Guid.NewGuid();
        var sinDetalle = () => CorreccionLiberacion.Crear(EmpresaId, Pedido, retenida, Guid.NewGuid(), TipoCorreccionLiberacion.Cliente, "  ", Corrige, Ahora);
        sinDetalle.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CL_DETALLE_INVALIDO");

        var iguales = () => CorreccionLiberacion.Crear(EmpresaId, Pedido, retenida, retenida, TipoCorreccionLiberacion.Cliente, "Cliente equivocado", Corrige, Ahora);
        iguales.Should().Throw<BusinessRuleException>().Where(e => e.Code == "CL_DECISIONES_IGUALES");
    }

    [Fact]
    public async Task El_listado_marca_la_retencion_reevaluada_y_la_decision_que_salio_de_la_correccion()
    {
        await using var db = Db();
        var factura = await LineaConFacturaAbiertaAsync(db, ClienteA, limite: 100_000m, facturado: 100_000m);
        var retenida = await DecidirAsync(db, ClienteA, 50_000m);
        factura.AplicarPago(80_000m);
        await db.SaveChangesAsync();
        var res = await ReevaluarAsync(db, retenida.Id, TipoCorreccionLiberacion.SaldoAplicado, "Cobro aplicado a la factura correcta");

        var lista = await new ListarDecisionesLiberacionHandler(db).Handle(new ListarDecisionesLiberacionQuery(Pedido), default);

        lista.Items.Single(d => d.Id == retenida.Id).ReevaluadaPorId.Should().Be(res.Nueva.Id);
        lista.Items.Single(d => d.Id == res.Nueva.Id).ReevaluaDecisionId.Should().Be(retenida.Id);
    }

    // ---------------------------------------------------------------- helpers

    private sealed class Empresa(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current => id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class Usuario(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "test";
    }

    private sealed class Reloj(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }

    private sealed class Publicador : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Clientes(params Guid[] conocidos) : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => Task.FromResult<ClienteRefDto?>(null);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult(conocidos.Contains(clienteId) ? new ClienteRefDto(clienteId, "XAXX010101000", "Cliente DEMO", false) : null);
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
            string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
    }

    private static CuentasPorCobrarDbContext Db()
    {
        var db = new CuentasPorCobrarDbContext(
            new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_m238_{Guid.NewGuid()}").Options,
            new Empresa(EmpresaId));
        db.ReglasLiberacionSerie.AddRange(
            ReglaLiberacionSerieConfiguration.SeriesMillet.Select(r => new ReglaLiberacionSerie(r.Id, r.Prefijo, r.Comportamiento)));
        db.SaveChanges();
        return db;
    }

    /// <summary>Línea de crédito del cliente y, si hay saldo, una factura abierta por <paramref name="facturado"/>.</summary>
    private static async Task<FacturaCartera> LineaConFacturaAbiertaAsync(
        CuentasPorCobrarDbContext db, Guid cliente, decimal limite, decimal facturado)
    {
        db.LineasCredito.Add(LineaCreditoAggregate.Crear(EmpresaId, cliente, "MXN", limite, OrigenLineaCredito.Solunion, plazoDias: 45));
        var factura = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), cliente, "XAXX010101000", "Cliente", Guid.NewGuid().ToString(),
            "FV-1", Math.Max(facturado, 0.01m), "MXN", "PPD", Ahora, Ahora.AddDays(45));
        if (facturado > 0) db.FacturasCartera.Add(factura);
        await db.SaveChangesAsync();
        return factura;
    }

    private static async Task<DecisionLiberacionResponse> DecidirAsync(
        CuentasPorCobrarDbContext db, Guid cliente, decimal monto, DateTimeOffset? ahora = null) =>
        await new DecidirLiberacionHandler(db, new Publicador(), new Empresa(EmpresaId), new Usuario(Coordinador),
                new Reloj(ahora ?? Ahora), new PedidosSinFacturarFalso())
            .Handle(new DecidirLiberacionCommand(Pedido, cliente, "MXN", monto, null), default);

    private static Task<ReevaluacionLiberacionResponse> ReevaluarAsync(
        CuentasPorCobrarDbContext db, Guid decisionId, TipoCorreccionLiberacion tipo, string detalle,
        Guid? clienteCorregido = null, Guid[]? clientesConocidos = null) =>
        new ReevaluarLiberacionRetenidaHandler(db, new Publicador(), new Empresa(EmpresaId), new Usuario(Corrige),
                new Reloj(Ahora.AddHours(1)), new PedidosSinFacturarFalso(), new Clientes(clientesConocidos ?? [ClienteA, ClienteB]))
            .Handle(new ReevaluarLiberacionRetenidaCommand(decisionId, tipo, detalle, clienteCorregido), default);
}
