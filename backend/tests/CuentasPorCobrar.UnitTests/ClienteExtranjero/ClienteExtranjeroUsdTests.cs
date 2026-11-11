using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Alertas;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorCobrar.Application.LineasCredito;
using Millet.CuentasPorCobrar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.LineaCredito;
using Millet.CuentasPorCobrar.Domain.Ports.Catalogos;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

using Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

namespace Millet.CuentasPorCobrar.UnitTests.ClienteExtranjero;

using LineaCreditoAggregate = Millet.CuentasPorCobrar.Domain.LineaCredito.LineaCredito;

/// <summary>
/// V-CXC-11 (86e3jvv78) · Reglas de cliente extranjero: línea en USD con plazo de EU (45) o
/// Canadá (60), bloqueo con su plazo + tolerancia, antigüedad en USD con su equivalente en
/// MXN, comisión bancaria menor a 50 USD (CA7.10) y el mismo cliente en cartera que en la
/// factura aunque los extranjeros compartan el RFC genérico.
/// </summary>
public sealed class ClienteExtranjeroUsdTests
{
    private const string RfcExtranjero = "XEXX010101000";
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteCanada = Guid.NewGuid();
    private static readonly Guid ClienteEu = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Autorizador = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    // --------------------------------------------------- Regla 1 · plazos por cliente

    [Theory]
    [InlineData(7)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    public void La_linea_acepta_los_plazos_nacionales_de_EU_Canada_y_la_excepcion_de_90(int plazo)
    {
        var resultado = new CrearLineaCreditoValidator().Validate(
            new CrearLineaCreditoCommand(ClienteCanada, "USD", 50_000m, OrigenLineaCredito.Interno, plazo));

        resultado.IsValid.Should().BeTrue();
        LineaCreditoAggregate.Crear(EmpresaId, ClienteCanada, "USD", 50_000m, OrigenLineaCredito.Interno, plazo)
            .PlazoDias.Should().Be(plazo);
    }

    // --------------------------------------------------- V-CXC-11-a · bloqueo con el plazo de Canadá

    [Theory]
    [InlineData(70, false)] // el caso de la ficha: 70 días sin pagar
    [InlineData(75, false)] // último día dentro de 60 + 15
    [InlineData(76, true)]  // primer día fuera de 60 + 15
    public async Task V_CXC_11_a_cliente_de_Canada_a_60_dias_no_se_bloquea_antes_de_60_mas_tolerancia(
        int diasSinPagar, bool bloqueada)
    {
        using var db = CrearDbContext();
        var lineaUsd = AgregarLinea(db, ClienteCanada, "USD", plazoDias: 60);
        AgregarFactura(db, ClienteCanada, "USD", diasDesdeTimbrado: diasSinPagar, plazoDias: 60, total: 12_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        var l = await db.LineasCredito.SingleAsync(x => x.Id == lineaUsd.Id);
        l.Estado.Should().Be(bloqueada ? EstadoLineaCredito.Bloqueada : EstadoLineaCredito.Activa);
        if (bloqueada) l.MotivoBloqueo.Should().Contain("plazo de 60 + 15 de tolerancia");
    }

    [Fact]
    public async Task V_CXC_11_a_la_factura_en_USD_no_bloquea_la_linea_en_MXN_del_mismo_cliente()
    {
        using var db = CrearDbContext();
        var lineaMxn = AgregarLinea(db, ClienteCanada, "MXN", plazoDias: 15);
        var lineaUsd = AgregarLinea(db, ClienteCanada, "USD", plazoDias: 60);
        AgregarFactura(db, ClienteCanada, "USD", diasDesdeTimbrado: 70, plazoDias: 60, total: 12_000m);
        await db.SaveChangesAsync();

        await Evaluar(db);

        (await db.LineasCredito.SingleAsync(x => x.Id == lineaMxn.Id)).Estado.Should().Be(EstadoLineaCredito.Activa,
            "70 días rebasarían el plazo de 15 + 15 de la línea en pesos, pero la factura es en dólares");
        (await db.LineasCredito.SingleAsync(x => x.Id == lineaUsd.Id)).Estado.Should().Be(EstadoLineaCredito.Activa);
    }

    // --------------------------------------------------- Mismo cliente en cartera que en la factura

    [Fact]
    public async Task La_factura_de_un_extranjero_entra_a_cartera_con_el_cliente_de_Facturacion_y_su_plazo_de_60()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, ClienteEu, "USD", plazoDias: 45);
        AgregarLinea(db, ClienteCanada, "USD", plazoDias: 60);
        await db.SaveChangesAsync();

        // Los dos comparten el RFC genérico; el primero por clave es el de EU.
        await Timbrar(db, PayloadExtranjero(clienteId: ClienteCanada));

        var f = await db.FacturasCartera.SingleAsync();
        f.ClienteId.Should().Be(ClienteCanada);
        f.Moneda.Should().Be("USD");
        f.FechaVencimiento.Should().Be(Ahora.AddDays(60));
    }

    [Fact]
    public async Task QA_el_extranjero_dado_de_alta_como_generico_entra_con_su_cliente_y_su_plazo()
    {
        // QA 7-nov (CXC-11): el alta de clientes marca «genérico» a todo RFC genérico, también XEXX. El evento trae su id.
        using var db = CrearDbContext();
        AgregarLinea(db, ClienteCanada, "USD", plazoDias: 60);
        await db.SaveChangesAsync();

        await Timbrar(db, PayloadExtranjero(clienteId: ClienteCanada), new FakeClientes(genericos: true));

        var f = await db.FacturasCartera.SingleAsync();
        f.ClienteId.Should().Be(ClienteCanada, "antes se descartaba por genérico y la factura vencía al timbrado");
        f.FechaVencimiento.Should().Be(Ahora.AddDays(60));
    }

    [Fact]
    public async Task El_publico_en_general_por_id_sigue_sin_ligarse_a_la_cartera()
    {
        using var db = CrearDbContext();
        await Timbrar(db, PayloadExtranjero(clienteId: Guid.NewGuid()) with { ReceptorRfc = "XAXX010101000" },
            new FakeClientes(genericos: true, rfcPorId: "XAXX010101000"));

        (await db.FacturasCartera.SingleAsync()).ClienteId.Should().BeNull("el público en general no participa del crédito");
    }

    [Fact]
    public async Task Sin_cliente_en_el_evento_el_RFC_generico_de_extranjero_no_se_liga_al_primer_extranjero()
    {
        using var db = CrearDbContext();
        AgregarLinea(db, ClienteEu, "USD", plazoDias: 45);
        await db.SaveChangesAsync();

        await Timbrar(db, PayloadExtranjero(clienteId: null));

        var f = await db.FacturasCartera.SingleAsync();
        f.ClienteId.Should().BeNull("el RFC genérico no identifica al cliente; se vincula desde la cartera");
        f.FechaVencimiento.Should().Be(Ahora);
    }

    // --------------------------------------------------- V-CXC-11-b · antigüedad en USD con su equivalente

    [Fact]
    public async Task V_CXC_11_b_la_antiguedad_en_USD_muestra_USD_y_su_equivalente_en_MXN()
    {
        using var db = CrearDbContext();
        AgregarFactura(db, ClienteCanada, "USD", diasDesdeTimbrado: 70, plazoDias: 60, total: 1_000m); // 10 días vencida
        AgregarFactura(db, ClienteCanada, "USD", diasDesdeTimbrado: 10, plazoDias: 60, total: 500m);   // por vencer
        await db.SaveChangesAsync();

        var r = await Antiguedad(db, new AntiguedadSaldosQuery(Moneda: "USD"), new FakeTipoCambio(("USD", 18.42m)));

        var fila = r.Filas.Should().ContainSingle().Subject;
        fila["moneda"].Should().Be("USD");
        fila["b1_15"].Should().Be(1_000m);
        fila["por_vencer"].Should().Be(500m);
        fila["total"].Should().Be(1_500m);
        fila["tipo_cambio"].Should().Be(18.42m);
        fila["vencido_mxn"].Should().Be(18_420m);
        fila["total_mxn"].Should().Be(27_630m);
        r.Totales!["total"].Should().Be(1_500m);
        r.Totales["total_mxn"].Should().Be(27_630m);
        r.Columnas.Should().Contain(c => c.Key == "total_mxn" && c.Label == "Total en MXN");
        r.FiltrosAplicados.Should().Contain(f => f.Label == "TC USD al corte" && f.Valor == "18.42 MXN (catálogo)");
    }

    [Fact]
    public async Task V_CXC_11_b_con_USD_y_MXN_juntos_solo_se_suma_el_equivalente_en_pesos()
    {
        using var db = CrearDbContext();
        AgregarFactura(db, ClienteCanada, "USD", diasDesdeTimbrado: 70, plazoDias: 60, total: 1_000m);
        AgregarFactura(db, ClienteCanada, "MXN", diasDesdeTimbrado: 20, plazoDias: 15, total: 5_000m);
        await db.SaveChangesAsync();

        var r = await Antiguedad(db, new AntiguedadSaldosQuery(), new FakeTipoCambio(("USD", 18.42m)));

        r.Filas.Should().HaveCount(2);
        r.Filas.Single(f => (string)f["moneda"]! == "MXN")["tipo_cambio"].Should().Be(1m);
        r.Totales!["total"].Should().BeNull("no se suman dólares con pesos");
        r.Totales["vencido"].Should().BeNull();
        r.Totales["vencido_mxn"].Should().Be(18_420m + 5_000m);
        r.Totales["total_mxn"].Should().Be(18_420m + 5_000m);
        r.Totales["numero_facturas"].Should().Be(2);
    }

    [Fact]
    public async Task V_CXC_11_b_sin_TC_en_el_catalogo_el_equivalente_queda_vacio_y_se_avisa()
    {
        using var db = CrearDbContext();
        AgregarFactura(db, ClienteCanada, "USD", diasDesdeTimbrado: 70, plazoDias: 60, total: 1_000m);
        await db.SaveChangesAsync();

        var r = await Antiguedad(db, new AntiguedadSaldosQuery(), new FakeTipoCambio());

        var fila = r.Filas.Should().ContainSingle().Subject;
        fila["total"].Should().Be(1_000m);
        fila["tipo_cambio"].Should().BeNull();
        fila["total_mxn"].Should().BeNull();
        r.Totales!["total_mxn"].Should().BeNull();
        r.FiltrosAplicados.Should().Contain(f => f.Label == "TC USD al corte" && f.Valor.StartsWith("sin TC"));
    }

    // --------------------------------------------------- CA7.10 · comisión bancaria de 48 USD

    [Fact]
    public async Task CA7_10_cliente_de_EU_paga_48_USD_menos_por_comision_la_factura_queda_saldada_sin_CFDI()
    {
        using var db = CrearDbContext();
        var factura = AgregarFactura(db, ClienteEu, "USD", diasDesdeTimbrado: 40, plazoDias: 45, total: 10_000m, uuid: "uuid-eu");
        await db.SaveChangesAsync();
        var publisher = new FakePublisher();

        var creada = await new CrearPropuestaAplicacionHandler(db, publisher, new FakeEmpresaContext(),
                Options.Create(new AplicacionPagosOptions()), new FakeClock(), new FakeUser(Proponente))
            .Handle(new CrearPropuestaAplicacionCommand(ClienteEu, "DEP-EU", 9_952m, "USD", "REM-EU",
                [new PropuestaFacturaLinea("uuid-eu", 10_000m, 1)], TipoAjuste: TipoAjusteCobro.ComisionBancaria,
                MotivoAjuste: "Comisión del banco del cliente"), default);
        creada.AjusteNoFiscal.Should().Be(-48m);

        await new AutorizarAjustePropuestaHandler(db, publisher, new FakeUser(Autorizador), new FakeClock(), new SoporteFijo(true))
            .Handle(new AutorizarAjustePropuestaCommand(creada.Id, creada.Version), default);
        var movimiento = Guid.NewGuid();
        await new ResolverPropuestaTesoreriaHandler(db, new FakeClock()).Handle(
            new(Guid.NewGuid(), EmpresaId, creada.Id, Guid.NewGuid(), Ahora, movimiento, null), default);
        await new ReciboPagoTimbradoHandler(db, new FakeClock(), NullLogger<ReciboPagoTimbradoHandler>.Instance)
            .Handle(new ReciboPagoTimbradoCommand(Guid.NewGuid(), new(EmpresaId, Ahora, Guid.NewGuid(), "REP-EU", 9_952m, 0,
                [new(factura.FacturaVentaId, 9_952m, 1, "USD", 48m)], movimiento)), default);

        // QA 7-nov: el REP relaciona lo cobrado (9,952, insoluto fiscal 48); el ajuste autorizado salda la factura en cartera.
        factura.Estado.Should().Be(EstadoFacturaCartera.Pagada);
        factura.SaldoPendiente.Should().Be(0m);
        (await db.MovimientosCartera.SingleAsync()).Importe.Should().Be(10_000m, "cuadra con la póliza #12, que abona lo aplicado");
        factura.MontoNc.Should().Be(0m, "la comisión no lleva nota de crédito");
        var propuesta = await db.PropuestasAplicacionPago.SingleAsync(p => p.Id == creada.Id);
        propuesta.TipoAjuste.Should().Be(TipoAjusteCobro.ComisionBancaria);
        propuesta.AjusteAutorizadoPor.Should().Be(Autorizador);
    }

    // --------------------------------------------------- helpers

    private static async Task Evaluar(CuentasPorCobrarDbContext db) =>
        await new EvaluarAlertasCarteraHandler(
                db, new FakePublisher(), Options.Create(new AlertasCarteraOptions()),
                new FakeClock(), NullLogger<EvaluarAlertasCarteraHandler>.Instance)
            .Handle(new EvaluarAlertasCarteraCommand(), CancellationToken.None);

    private static Task<ReporteJsonResponse> Antiguedad(
        CuentasPorCobrarDbContext db, AntiguedadSaldosQuery query, ITipoCambioCarteraReadPort tiposCambio) =>
        new AntiguedadSaldosHandler(db, new BucketsAntiguedadCxc([15, 30, 60, 90]), new FakeClientes(), new FakeClock(), tiposCambio)
            .Handle(query, CancellationToken.None);

    private static Task Timbrar(CuentasPorCobrarDbContext db, FacturaVentaTimbradaPayload payload, FakeClientes? clientes = null) =>
        new FacturaVentaTimbradaHandler(db, clientes ?? new FakeClientes(), new FakeClock(), NullLogger<FacturaVentaTimbradaHandler>.Instance)
            .Handle(new FacturaVentaTimbradaCommand(Guid.NewGuid(), payload), CancellationToken.None);

    private static FacturaVentaTimbradaPayload PayloadExtranjero(Guid? clienteId) => new(
        EmpresaId, Ahora, Guid.NewGuid(), Guid.NewGuid().ToString(), 12_000m, "USD", null,
        RfcExtranjero, "Cliente DEMO de Canadá", "FA-DEMO", "PPD", Ahora, ClienteId: clienteId);

    private static LineaCreditoAggregate AgregarLinea(CuentasPorCobrarDbContext db, Guid clienteId, string moneda, int plazoDias)
    {
        var linea = LineaCreditoAggregate.Crear(EmpresaId, clienteId, moneda, 1_000_000m, OrigenLineaCredito.Interno, plazoDias);
        db.LineasCredito.Add(linea);
        return linea;
    }

    private static FacturaCartera AgregarFactura(
        CuentasPorCobrarDbContext db, Guid clienteId, string moneda, int diasDesdeTimbrado, int plazoDias, decimal total,
        string? uuid = null)
    {
        var timbrado = Ahora.AddDays(-diasDesdeTimbrado);
        var factura = FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), clienteId, RfcExtranjero, "Cliente DEMO extranjero",
            uuid ?? Guid.NewGuid().ToString(), "FA-DEMO", total, moneda, "PPD",
            fechaTimbrado: timbrado, fechaVencimiento: timbrado.AddDays(plazoDias));
        db.FacturasCartera.Add(factura);
        return factura;
    }

    private static CuentasPorCobrarDbContext CrearDbContext() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase($"cxc_v_cxc_11_{Guid.NewGuid()}").Options, new FakeEmpresaContext());

    /// <summary>
    /// Como el adapter real: por RFC gana el primer cliente por clave (EU); por id, el exacto. <paramref name="genericos"/>
    /// = el cliente está marcado como genérico, como lo deja el alta de clientes con un RFC genérico.
    /// </summary>
    private sealed class FakeClientes(bool genericos = false, string rfcPorId = RfcExtranjero) : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(new ClienteRefDto(ClienteEu, rfc, "Cliente DEMO de EU", false));
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(new ClienteRefDto(clienteId, rfcPorId, "Cliente DEMO", genericos));
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
            string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
    }

    private sealed class FakeTipoCambio(params (string Moneda, decimal Valor)[] valores) : ITipoCambioCarteraReadPort
    {
        public Task<decimal?> ValorEnMxnAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken) =>
            Task.FromResult(valores.Where(v => v.Moneda == moneda).Select(v => (decimal?)v.Valor).FirstOrDefault());
    }

    private sealed class FakeUser(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "Usuario de prueba";
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
        public Guid? Current => EmpresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
