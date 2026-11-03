using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Cajas.Terminal;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// 86e3vr1tf · Parte A: archivo diario de la terminal de tarjetas. Datos
/// FICTICIOS con el layout «Transacciones por día» de Getnet más las columnas
/// supuestas de comisión (a validar con el reporte de comisiones del banco).
/// </summary>
public sealed class TerminalTarjetaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Dia = new(2026, 10, 1);

    private const string Encabezado =
        "<tr><th>No. Operación</th><th>Usuario</th><th>Fecha</th><th>REFERENCIA</th><th>Nombre del cliente</th>" +
        "<th>Importe</th><th>Moneda</th><th>TDC</th><th>Estatus</th><th># de Auth.</th><th>F.Pago</th><th>Canal</th>" +
        "<th>Operación</th><th>3DSecure</th><th>Banco Adquirente</th>{0}</tr>";

    private const string ColumnasComision = "<th>Comisión</th><th>IVA Comisión</th><th>Neto</th><th>Folio de liquidación</th>";

    private static string Renglon(string operacion, string importe, string auth, string estatus = "Aprobada",
        string? comision = null, string? iva = null, string? neto = null, string? folio = null) =>
        $"<tr><td>{operacion}</td><td>PRUEBA</td><td>01/10 12:30</td><td>REF-PRUEBA</td><td>Cliente Prueba</td>" +
        $"<td>{importe}</td><td>MXN</td><td>4321</td><td>{estatus}</td><td>{auth}</td><td>Contado</td><td>Ventas Móviles</td>" +
        $"<td>VENTA</td><td>NO</td><td>Getnet</td>" +
        (comision is null ? "" : $"<td>{comision}</td><td>{iva}</td><td>{neto}</td><td>{folio}</td>") + "</tr>";

    private static byte[] Html(bool conComision, params string[] renglones) => Encoding.Latin1.GetBytes(
        "<html><body><table>" + Encabezado.Replace("{0}", conComision ? ColumnasComision : "", StringComparison.Ordinal) +
        string.Concat(renglones) + "</table></body></html>");

    [Fact]
    public async Task Criterio_1_empareja_por_autorizacion_y_publica_la_liquidacion_con_neto_comision_e_iva()
    {
        using var db = NewDb();
        var cobro = await CobrarTarjetaAsync(db, 10_000m, "123456");
        var eventos = new FakeIntegrationEventPublisher();

        var r = await Cargar(db, eventos, Html(true,
            Renglon("900000000001", "$ 10,000.00", "123456", comision: "$ 200.00", iva: "$ 32.00", neto: "$ 9,768.00", folio: "LIQ-PRUEBA-1")));

        r.Nuevos.Should().Be(1);
        r.Emparejados.Should().Be(1);
        r.Liquidaciones.Should().Be(1);
        var t = await db.TransaccionesTerminal.SingleAsync();
        t.Estado.Should().Be(EstadoTransaccionTerminal.Emparejada);
        t.CobroMostradorId.Should().Be(cobro);
        var e = eventos.Publicados.OfType<TerminalLiquidacionRegistradaIntegrationEvent>().Single();
        (e.Bruto, e.Comision, e.IvaComision, e.Neto).Should().Be((10_000m, 200m, 32m, 9_768m));
        e.FolioLiquidacion.Should().Be("LIQ-PRUEBA-1");
        e.Ventas.Should().ContainSingle().Which.CobroMostradorId.Should().Be(cobro);
    }

    [Fact]
    public async Task Criterio_2_recargar_el_mismo_archivo_no_agrega_renglones_ni_eventos()
    {
        using var db = NewDb();
        await CobrarTarjetaAsync(db, 10_000m, "123456");
        var archivo = Html(true,
            Renglon("900000000001", "$ 10,000.00", "123456", comision: "$ 200.00", iva: "$ 32.00", neto: "$ 9,768.00", folio: "LIQ-PRUEBA-1"));
        await Cargar(db, new FakeIntegrationEventPublisher(), archivo);
        var eventos = new FakeIntegrationEventPublisher();

        var segunda = await Cargar(db, eventos, archivo);

        segunda.Nuevos.Should().Be(0);
        segunda.Duplicados.Should().Be(1);
        segunda.Liquidaciones.Should().Be(0);
        eventos.Publicados.Should().BeEmpty();
        (await db.TransaccionesTerminal.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Criterio_3_venta_sin_cobro_queda_sin_emparejar_y_sin_evento()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var eventos = new FakeIntegrationEventPublisher();

        var r = await Cargar(db, eventos, Html(true,
            Renglon("900000000002", "$ 500.00", "654321", comision: "$ 10.00", iva: "$ 1.60", neto: "$ 488.40", folio: "LIQ-PRUEBA-2")));

        r.SinEmparejar.Should().Be(1);
        (await db.TransaccionesTerminal.SingleAsync()).Estado.Should().Be(EstadoTransaccionTerminal.SinEmparejar);
        eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task Venta_sin_cobro_se_empareja_al_reintentar_despues_de_registrar_el_cobro()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var eventos = new FakeIntegrationEventPublisher();
        await Cargar(db, eventos, Html(false, Renglon("900000000003", "$ 1,500.00", "777777")));
        (await db.TransaccionesTerminal.SingleAsync()).Estado.Should().Be(EstadoTransaccionTerminal.SinEmparejar);

        await CobrarTarjetaAsync(db, 1_500m, "777777", sembrarSesion: false);
        var r = await Handlers(db, eventos).Handle(new ReemparejarTerminalCommand(), default);

        r.Emparejados.Should().Be(1);
        (await db.TransaccionesTerminal.SingleAsync()).Estado.Should().Be(EstadoTransaccionTerminal.Emparejada);
        // Sin columnas de comisión el archivo empareja pero no genera liquidación.
        eventos.Publicados.OfType<TerminalLiquidacionRegistradaIntegrationEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Venta_rechazada_por_el_banco_queda_aparte()
    {
        using var db = NewDb();
        await CobrarTarjetaAsync(db, 300m, "111111");

        var r = await Cargar(db, new FakeIntegrationEventPublisher(), Html(false,
            Renglon("900000000004", "$ 300.00", "", estatus: "Rechazada")));

        r.Rechazados.Should().Be(1);
        (await db.TransaccionesTerminal.SingleAsync()).Estado.Should().Be(EstadoTransaccionTerminal.Rechazada);
    }

    [Fact]
    public void Lector_acepta_csv_con_mapeo_configurable_y_avisa_si_faltan_columnas()
    {
        var opciones = new TerminalTarjetaOptions();
        opciones.Columnas.NoOperacion = "Operacion";
        opciones.Columnas.Autorizacion = "Autorizacion";
        var csv = Encoding.UTF8.GetBytes(
            "Operacion,Fecha,Importe,Moneda,TDC,Estatus,Autorizacion\n" +
            "900000000005,01/10 09:15,\"$ 2,000.50\",MXN,1111,Aprobada,222222\n");

        var renglones = LectorArchivoTerminal.Leer(csv, Dia, opciones);

        var r = renglones.Should().ContainSingle().Subject;
        r.Importe.Should().Be(2_000.50m);
        r.FechaHora.Should().Be(new DateTime(2026, 10, 1, 9, 15, 0));
        r.Autorizacion.Should().Be("222222");
        r.Aprobada.Should().BeTrue();

        var sinColumnas = () => LectorArchivoTerminal.Leer(Encoding.UTF8.GetBytes("A,B\n1,2\n"), Dia, new TerminalTarjetaOptions());
        sinColumnas.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("TERMINAL_ENCABEZADOS");
    }

    // ---- 86e3xt2q8: archivo OneGMR TRST de Getnet (nombres reales de la guía «Getnet One GMR», jun-2025) ----

    private const string EncabezadoGmr =
        "NUM_MERCHANT_LEGAL_DOCUMENT,NAM_MERCHANT,COD_SELLER,NAM_SELLER,NUM_BANK_ACCOUNT,COD_BANK_ACCOUNT_CURRENCY,ID_TERMINAL," +
        "NUM_CARD,DES_FINANCIAL_MOVEMENTS_TYPE,DES_TRANSACTION_TYPE,DES_TRANSACTION_STATUS,ID_TRANSACTION,COD_AUTHORIZATION," +
        "COD_RRN,DAT_TRANSACTION,TIM_TRANSACTION,ID_SETTLEMENT,DAT_SETTLEMENT,DAT_SETTLED,AMT_GROSS,COD_CURRENCY,AMT_NET," +
        "AMT_NET_CURRENCY,AMT_TOTAL_FEE,AMT_TOTAL_VAT_TAX\n";

    private static string RenglonGmr(string id, string tipo, string estatus, string auth, string bruto, string neto, string fee,
        string iva, string settlement = "202610026740289484GNMXPURCC0", string settled = "2026-10-02") =>
        $"XPT9999999A10,PRUEBA,1,PRUEBA,0,484,T001,4321-56XX-XXXX-0987,{tipo},Purchase,{estatus},{id},{auth}," +
        $"'227320004374,2026-10-01,12:28:29,{settlement},2026-10-02,{settled},{bruto},484,{neto},484,{fee},{iva}\n";

    [Fact]
    public async Task Archivo_gmr_trst_empareja_y_liquida_con_los_nombres_reales_de_getnet()
    {
        using var db = NewDb();
        // El voucher trae 021747 y el GMR manda COD_AUTHORIZATION como número: 21747.
        var cobro = await CobrarTarjetaAsync(db, 10_000m, "021747");
        var eventos = new FakeIntegrationEventPublisher();
        var csv = Encoding.UTF8.GetBytes(EncabezadoGmr
            + RenglonGmr("45f9b223-ce28-4710-9300-5bd13f915555", "Transaction", "Captured", "21747", "10000", "9768", "200", "32")
            + RenglonGmr("", "Billing", "", "", "150", "174", "0", "24")
            + "ENDOFFILE_000000002_000000001\n");

        var r = await Handlers(db, eventos).Handle(
            new CargarArchivoTerminalCommand("GMR_MX_GETNET_9999999_20261002_R_TRST_001.CSV", csv, Dia), default);

        r.Renglones.Should().Be(1, "el cargo mensual (Billing) y el renglón ENDOFFILE no son ventas");
        r.Emparejados.Should().Be(1);
        r.Liquidaciones.Should().Be(1);
        var t = await db.TransaccionesTerminal.SingleAsync();
        (t.Ultimos4, t.Moneda, t.FechaHora).Should().Be(("0987", "MXN", new DateTime(2026, 10, 1, 12, 28, 29)));
        t.CobroMostradorId.Should().Be(cobro);
        var e = eventos.Publicados.OfType<TerminalLiquidacionRegistradaIntegrationEvent>().Single();
        (e.Bruto, e.Comision, e.IvaComision, e.Neto).Should().Be((10_000m, 200m, 32m, 9_768m));
        e.FolioLiquidacion.Should().Be("202610026740289484GNMXPURCC0");
        e.Fecha.Should().Be(new DateOnly(2026, 10, 2), "el cargo es del día en que el banco liquidó (DAT_SETTLED)");
    }

    [Fact]
    public void Lector_gmr_marca_rechazadas_las_ventas_denegadas()
    {
        var csv = Encoding.UTF8.GetBytes(EncabezadoGmr
            + RenglonGmr("op-denegada", "Transaction", "Denied", "000000", "500", "0", "0", "0"));

        var r = LectorArchivoTerminal.Leer(csv, Dia, new TerminalTarjetaOptions()).Should().ContainSingle().Subject;

        r.Aprobada.Should().BeFalse();
        r.FechaLiquidacion.Should().Be(new DateOnly(2026, 10, 2));
    }

    // ---- helpers ----

    private static Task<CargaTerminalResponse> Cargar(FacturacionDbContext db, FakeIntegrationEventPublisher eventos, byte[] archivo) =>
        Handlers(db, eventos).Handle(new CargarArchivoTerminalCommand("terminal-prueba.xls", archivo, Dia), default);

    private static TerminalTarjetaHandlers Handlers(FacturacionDbContext db, FakeIntegrationEventPublisher eventos) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora), eventos,
            new Opciones(new TerminalTarjetaOptions()));

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    private static async Task SembrarSesionAsync(FacturacionDbContext db)
    {
        var caja = Caja.Crear(Empresa, "Caja Prueba", null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(CajaSesion.Abrir(Empresa, caja.Id, SucursalId, Cajero, 0m, Dia, Ahora.AddHours(-8), null));
        await db.SaveChangesAsync();
    }

    /// <summary>Factura PUE timbrada cobrada con tarjeta de crédito (forma 04) con su voucher.</summary>
    private static async Task<Guid> CobrarTarjetaAsync(FacturacionDbContext db, decimal total, string autorizacion, bool sembrarSesion = true)
    {
        if (sembrarSesion) await SembrarSesionAsync(db);
        var fv = FacturaVenta.CrearBorrador(Empresa, $"F-{autorizacion}", 1, SucursalId, null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"),
            "PUE", "04", "MXN", null, 2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, total, 0m, "01", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        var r = await new RegistrarCobroMostradorHandler(
                db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
                new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(), new FakeIntegrationEventPublisher(),
                new FakeCatalogosSatReadPort())
            .Handle(new RegistrarCobroMostradorCommand(fv.Id,
                [new CobroFormaPagoInput("04", fv.Total, autorizacion, TarjetaUltimos4: "4321")]), default);
        db.ChangeTracker.Clear();
        return r.Id;
    }

    private sealed class Opciones(TerminalTarjetaOptions valor) : IOptionsMonitor<TerminalTarjetaOptions>
    {
        public TerminalTarjetaOptions CurrentValue => valor;
        public TerminalTarjetaOptions Get(string? name) => valor;
        public IDisposable? OnChange(Action<TerminalTarjetaOptions, string?> listener) => null;
    }
}
