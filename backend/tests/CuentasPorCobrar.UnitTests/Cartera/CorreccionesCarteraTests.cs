using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.Cartera;
using Millet.CuentasPorCobrar.Application.Conciliacion;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Conciliacion;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cartera;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// M-237 (CXC-CARTERA/corregir): corregir el plazo de una factura (recalcula vencimiento y antigüedad, rastro antes/después) y
/// revertir un movimiento mal aplicado con motivo y autorización de otra persona. Datos genéricos.
/// </summary>
public sealed class CorreccionesCarteraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Solicita = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Autoriza = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    // ------------------------------------------------------------------ plazo

    [Fact]
    public async Task Corregir_el_plazo_recalcula_el_vencimiento_y_deja_el_rastro_antes_despues()
    {
        using var db = CrearDb();
        var factura = AgregarFactura(db, diasDesdeTimbrado: 10, plazoDias: 30);
        await db.SaveChangesAsync();
        var timbrado = factura.FechaTimbrado;

        var r = await new CorregirPlazoFacturaHandler(db, new Usuario(Solicita, "Persona A"), new Reloj())
            .Handle(new CorregirPlazoFacturaCommand(factura.Id, 45, "Se pactó a 45 días"), default);

        var f = await db.FacturasCartera.SingleAsync();
        f.FechaVencimiento.Should().Be(timbrado.AddDays(45));
        f.PlazoDias.Should().Be(45);
        f.PlazoCorregidoDias.Should().Be(45);
        r.PlazoAnteriorDias.Should().Be(30);
        r.PlazoNuevoDias.Should().Be(45);
        r.VencimientoAnterior.Should().Be(timbrado.AddDays(30));
        r.VencimientoNuevo.Should().Be(timbrado.AddDays(45));
        r.SolicitadaPorNombre.Should().Be("Persona A");
        r.Motivo.Should().Be("Se pactó a 45 días");
        r.Estado.Should().Be("Aplicada");
    }

    [Theory]
    [InlineData(30, "x", "FC_PLAZO_SIN_CAMBIO")]
    [InlineData(400, "x", "FC_PLAZO_INVALIDO")]
    [InlineData(45, " ", "CC_MOTIVO_VACIO")]
    public async Task Plazo_sin_cambio_fuera_de_rango_o_sin_motivo_se_rechaza(int plazo, string motivo, string codigo)
    {
        using var db = CrearDb();
        var factura = AgregarFactura(db, 10, plazoDias: 30);
        await db.SaveChangesAsync();

        var act = () => new CorregirPlazoFacturaHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new CorregirPlazoFacturaCommand(factura.Id, plazo, motivo), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == codigo);
        (await db.CorreccionesCartera.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task La_antiguedad_usa_el_vencimiento_corregido()
    {
        using var db = CrearDb();
        // Timbrada hace 40 días a 30 días: vencida 10 días. Con 60 días ya no está vencida.
        var factura = AgregarFactura(db, diasDesdeTimbrado: 40, plazoDias: 30);
        await db.SaveChangesAsync();

        var antes = await Antiguedad(db);
        ((decimal)antes.Filas.Single()["vencido"]!).Should().Be(10_000m);

        await new CorregirPlazoFacturaHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new CorregirPlazoFacturaCommand(factura.Id, 60, "Plazo pactado"), default);

        var despues = await Antiguedad(db);
        ((decimal)despues.Filas.Single()["vencido"]!).Should().Be(0m);
    }

    [Fact]
    public async Task El_estado_de_cuenta_muestra_la_correccion_con_quien_y_por_que()
    {
        using var db = CrearDb();
        var factura = AgregarFactura(db, 10, plazoDias: 30);
        await db.SaveChangesAsync();
        await new CorregirPlazoFacturaHandler(db, new Usuario(Solicita, "Persona A"), new Reloj())
            .Handle(new CorregirPlazoFacturaCommand(factura.Id, 45, "Se pactó a 45 días"), default);

        var fila = (await EstadoCuenta(db)).Filas.Single(f => (string?)f["tipo"] == "Corrección de plazo");

        var detalle = (string)fila["detalle"]!;
        detalle.Should().Contain("Plazo 30 → 45 días").And.Contain("Persona A").And.Contain("Se pactó a 45 días");
        fila["referencia"].Should().Be("FV-DEMO");
    }

    // ------------------------------------------------------------------ bloqueo C7.5 / alertas

    [Fact]
    public async Task El_bloqueo_C75_usa_el_plazo_corregido_si_es_mayor()
    {
        using var db = CrearDb();
        var linea = AgregarLinea(db, plazoDias: 15);
        var factura = AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15); // sin corrección bloquearía (15 + 15 < 31)
        await db.SaveChangesAsync();
        await new CorregirPlazoFacturaHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new CorregirPlazoFacturaCommand(factura.Id, 60, "Plazo pactado"), default);

        await EvaluarAlertas(db);

        (await db.LineasCredito.SingleAsync(l => l.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);
    }

    [Fact]
    public async Task El_bloqueo_C75_usa_el_plazo_corregido_si_es_menor()
    {
        using var db = CrearDb();
        var linea = AgregarLinea(db, plazoDias: 60);
        var factura = AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 60); // sin corrección no bloquea (60 + 15 > 31)
        await db.SaveChangesAsync();
        await new CorregirPlazoFacturaHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new CorregirPlazoFacturaCommand(factura.Id, 5, "Plazo pactado"), default);

        await EvaluarAlertas(db);

        (await db.LineasCredito.SingleAsync(l => l.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);
    }

    [Fact]
    public async Task Sin_correccion_el_bloqueo_C75_queda_igual_que_antes()
    {
        using var db = CrearDb();
        var linea = AgregarLinea(db, plazoDias: 15);
        AgregarFactura(db, diasDesdeTimbrado: 31, plazoDias: 15);
        await db.SaveChangesAsync();

        await EvaluarAlertas(db);

        (await db.LineasCredito.SingleAsync(l => l.Id == linea.Id)).Estado.Should().Be(EstadoLineaCredito.Bloqueada);
        BloqueoPorVencimiento.FacturasQueRebasan(
                new (DateTimeOffset, int?)[] { (Ahora.AddDays(-31), null) }, 15, 15, DateOnly.FromDateTime(Ahora.UtcDateTime))
            .Should().Equal(BloqueoPorVencimiento.FacturasQueRebasan(
                new[] { Ahora.AddDays(-31) }, 15, 15, DateOnly.FromDateTime(Ahora.UtcDateTime)));
    }

    // ------------------------------------------------------------------ reversa de movimiento

    [Fact]
    public async Task La_reversa_se_solicita_sin_mover_la_cartera_y_otra_persona_la_autoriza()
    {
        using var db = CrearDb();
        var (factura, mov) = await FacturaConPago(db, 4_000m);

        var sol = await new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "Persona A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(mov.Id, "Se aplicó a la factura equivocada"), default);
        sol.Estado.Should().Be("Pendiente");
        (await db.FacturasCartera.SingleAsync()).SaldoPendiente.Should().Be(6_000m, "la cartera no cambia hasta que se autoriza");

        var aut = await new DecidirReversaMovimientoHandler(db, new Usuario(Autoriza, "Persona B"), new Reloj())
            .Handle(new DecidirReversaMovimientoCommand(sol.Id, true, null), default);

        aut.Estado.Should().Be("Aplicada");
        aut.DecididaPorNombre.Should().Be("Persona B");
        (await db.FacturasCartera.SingleAsync()).SaldoPendiente.Should().Be(10_000m);
        (await db.MovimientosCartera.SingleAsync()).Revertido.Should().BeTrue();

        // El estado de cuenta ya no cuenta el pago y deja el rastro con quién pidió, quién autorizó y por qué.
        var edo = await EstadoCuenta(db);
        edo.Filas.Should().NotContain(f => (string?)f["tipo"] == "Pago");
        var detalle = (string)edo.Filas.Single(f => (string?)f["tipo"] == "Reversa de movimiento")["detalle"]!;
        detalle.Should().Contain("Persona A").And.Contain("Persona B").And.Contain("factura equivocada");
        edo.Totales!["saldo_documento"].Should().Be(10_000m);
    }

    [Fact]
    public async Task Quien_solicita_no_autoriza_su_propia_reversa()
    {
        using var db = CrearDb();
        var (_, mov) = await FacturaConPago(db, 4_000m);
        var sol = await new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(mov.Id, "Error"), default);

        var act = () => new DecidirReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new DecidirReversaMovimientoCommand(sol.Id, true, null), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CC_MISMA_PERSONA");
        (await db.MovimientosCartera.SingleAsync()).Revertido.Should().BeFalse();
    }

    [Fact]
    public async Task Rechazar_pide_motivo_y_no_cambia_la_cartera()
    {
        using var db = CrearDb();
        var (_, mov) = await FacturaConPago(db, 4_000m);
        var sol = await new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(mov.Id, "Error"), default);
        var handler = new DecidirReversaMovimientoHandler(db, new Usuario(Autoriza, "B"), new Reloj());

        var sinMotivo = () => handler.Handle(new DecidirReversaMovimientoCommand(sol.Id, false, " "), default);
        await sinMotivo.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CC_MOTIVO_RECHAZO_VACIO");

        var r = await handler.Handle(new DecidirReversaMovimientoCommand(sol.Id, false, "El pago sí era de esa factura"), default);
        r.Estado.Should().Be("Rechazada");
        (await db.FacturasCartera.SingleAsync()).SaldoPendiente.Should().Be(6_000m);
        (await db.MovimientosCartera.SingleAsync()).Revertido.Should().BeFalse();
    }

    [Fact]
    public async Task Un_pago_con_REP_no_se_revierte_desde_aqui_y_no_se_duplica_la_solicitud()
    {
        using var db = CrearDb();
        var (factura, mov) = await FacturaConPago(db, 4_000m);
        var duplicada = await new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(mov.Id, "Error"), default);
        var otra = () => new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(mov.Id, "Error"), default);
        await otra.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CC_YA_PENDIENTE");
        duplicada.Estado.Should().Be("Pendiente");

        // Pago ligado a un REP timbrado de una propuesta de aplicación.
        var rep = Guid.NewGuid();
        var pagoRep = new MovimientoCartera(EmpresaId, factura.Id, TipoMovimientoCartera.Pago, rep, 1_000m, Ahora);
        db.MovimientosCartera.Add(pagoRep);
        var propuesta = PropuestaConRep(factura.Id, rep);
        db.PropuestasAplicacionPago.Add(propuesta);
        await db.SaveChangesAsync();

        var act = () => new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(pagoRep.Id, "Error"), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CC_PAGO_CON_REP");
    }

    [Fact]
    public async Task Una_nota_de_credito_mal_aplicada_se_revierte_y_regresa_el_saldo()
    {
        using var db = CrearDb();
        var factura = AgregarFactura(db, 10, 30);
        factura.AplicarNotaCredito(2_000m);
        var mov = new MovimientoCartera(EmpresaId, factura.Id, TipoMovimientoCartera.NotaCredito, Guid.NewGuid(), 2_000m, Ahora);
        db.MovimientosCartera.Add(mov);
        await db.SaveChangesAsync();
        var sol = await new SolicitarReversaMovimientoHandler(db, new Usuario(Solicita, "A"), new Reloj())
            .Handle(new SolicitarReversaMovimientoCommand(mov.Id, "NC de otra factura"), default);

        await new DecidirReversaMovimientoHandler(db, new Usuario(Autoriza, "B"), new Reloj())
            .Handle(new DecidirReversaMovimientoCommand(sol.Id, true, null), default);

        (await db.FacturasCartera.SingleAsync()).MontoNc.Should().Be(0m);
    }

    [Fact]
    public void El_movimiento_revertido_deja_diferencia_visible_en_la_conciliacion_contra_Facturacion()
    {
        var facturaVenta = Guid.NewGuid();
        var carteraId = Guid.NewGuid();
        var origen = Guid.NewGuid();
        var dia = new ComprobantesDelDia(
            [new FacturaFacturacion(EmpresaId, facturaVenta, "FV-DEMO", "uuid", "VGL860910IU4", "Cliente DEMO", 10_000m, false)],
            [new AplicacionFacturacion(EmpresaId, TipoAplicacionFacturacion.CobroMostrador, origen, "CM-1", "uuid-cm",
                facturaVenta, "VGL860910IU4", "Cliente DEMO", 4_000m, false)]);

        var diferencias = ComparadorConciliacion.Comparar(dia,
            [new FacturaEnCartera(carteraId, facturaVenta, 10_000m, EstadoFacturaCartera.Abierta)],
            [new MovimientoEnCartera(carteraId, TipoMovimientoCartera.Pago, origen, 4_000m, Revertido: true)]);

        diferencias.Should().ContainSingle(d => d.Tipo == TipoDiferenciaConciliacion.CanceladoSoloEnCartera);
    }

    // ------------------------------------------------------------------ helpers

    private static async Task<(FacturaCartera, MovimientoCartera)> FacturaConPago(CuentasPorCobrarDbContext db, decimal importe)
    {
        var factura = AgregarFactura(db, 10, 30);
        factura.AplicarPago(importe);
        var mov = new MovimientoCartera(EmpresaId, factura.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), importe, Ahora);
        db.MovimientosCartera.Add(mov);
        await db.SaveChangesAsync();
        return (factura, mov);
    }

    private static PropuestaAplicacionPago PropuestaConRep(Guid facturaCarteraId, Guid reciboPagoId)
    {
        var p = PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-1", 1_000m, "MXN", "REM-1",
            [("uuid-1", facturaCarteraId, 1_000m, 1)], 50m, Solicita);
        p.Confirmar(Autoriza, Ahora, Guid.NewGuid());
        p.MarcarReppTimbrado(reciboPagoId);
        return p;
    }

    private static FacturaCartera AgregarFactura(CuentasPorCobrarDbContext db, int diasDesdeTimbrado, int plazoDias)
    {
        var timbrado = Ahora.AddDays(-diasDesdeTimbrado);
        var factura = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente DEMO",
            Guid.NewGuid().ToString(), "FV-DEMO", 10_000m, "MXN", "PPD", timbrado, timbrado.AddDays(plazoDias));
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static LineaCreditoAggregate AgregarLinea(CuentasPorCobrarDbContext db, int plazoDias)
    {
        var linea = LineaCreditoAggregate.Crear(EmpresaId, ClienteId, "MXN", 1_000_000m, OrigenLineaCredito.Interno, plazoDias);
        db.LineasCredito.Add(linea);
        return linea;
    }

    private static Task<int> EvaluarAlertas(CuentasPorCobrarDbContext db) =>
        new EvaluarAlertasCarteraHandler(db, new Publicador(), Options.Create(new AlertasCarteraOptions()),
            new Reloj(), NullLogger<EvaluarAlertasCarteraHandler>.Instance)
            .Handle(new EvaluarAlertasCarteraCommand(), CancellationToken.None);

    private static Task<ReporteJsonResponse> Antiguedad(CuentasPorCobrarDbContext db) =>
        new AntiguedadSaldosHandler(db, new BucketsAntiguedadCxc([15, 30, 60, 90]), new Clientes(), new Reloj())
            .Handle(new AntiguedadSaldosQuery(), CancellationToken.None);

    private static Task<ReporteJsonResponse> EstadoCuenta(CuentasPorCobrarDbContext db) =>
        new EstadoCuentaClienteHandler(db, new Anticipos(), new Clientes(), new Reloj())
            .Handle(new EstadoCuentaClienteQuery(ClienteId), CancellationToken.None);

    private static CuentasPorCobrarDbContext CrearDb() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_m237_{Guid.NewGuid()}").Options, new EmpresaCtx());

    private sealed class Usuario(Guid id, string nombre) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => nombre;
    }

    private sealed class Reloj : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class Publicador : IIntegrationEventPublisher
    {
        public Task PublishAsync(object integrationEvent, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Anticipos : IFacturacionAnticiposReadPort
    {
        public Task<IReadOnlyList<AnticipoSaldoClienteDto>> ListarPorClienteAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AnticipoSaldoClienteDto>>([]);
    }

    private sealed class Clientes : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => Task.FromResult<ClienteRefDto?>(null);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(new ClienteRefDto(clienteId, "VGL860910IU4", "Cliente DEMO", EsGenerico: false));
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
            string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
    }

    private sealed class EmpresaCtx : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
