using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Application.Reportes.CierreDia;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Cancelaciones;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Adapters;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Reportes;

/// <summary>
/// Cierre del día por sucursal (F6.7; Manual de Sucursales §2.4): monitor de
/// estatus de CFDI (CA6.18) y conciliación facturado contra ingresado
/// (CA6.18, F6.7-a), por sucursal, día y cajero, con el día leído en la zona
/// horaria de la sucursal (Cancún, UTC−5).
/// </summary>
public sealed class CierreDiaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Beto = Guid.NewGuid();
    private static readonly Guid Cancun = Guid.NewGuid();
    private static readonly DateOnly Dia = new(2026, 10, 23);

    /// <summary>13:00 en Cancún.</summary>
    private static readonly DateTimeOffset Ahora = new(2026, 10, 23, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static FakeSucursalesReadPort Sucursales() => new(zonaHoraria: "America/Cancun", nombre: "CUN — Cancún");

    private static FakeUsuariosReadPort Usuarios() =>
        new(new Dictionary<Guid, string> { [Ana] = "Ana Cajera", [Beto] = "Beto Cajero" });

    private static DatosFiscalesReceptor Receptor() =>
        new("AAA010101AAA", "Cliente Mostrador", "601", "77500", "G03", "MEX", false);

    private static DatosFiscalesEmisor Emisor() => new("BBB010101BBB", "Millet", "601", "77500");

    private static FacturaVenta Factura(
        decimal valor, string folio, Guid? emisor = null, string metodoPago = "PUE",
        DateTimeOffset? timbrada = null, bool timbrar = true)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, Cancun, null, emisor ?? Ana, Receptor(), Emisor(),
            metodoPago, metodoPago == "PPD" ? "99" : "01", "MXN", null, 2026, 10, 1,
            ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "01", 0m, null, null);
        fv.RecalcularTotales();
        if (!timbrar)
            return fv;
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), "SELLO-CFDI-12345678", null, null, timbrada ?? Ahora, null, null);
        return fv;
    }

    private static async Task<CajaSesion> AbrirSesionAsync(FacturacionDbContext db, Guid cajero, DateOnly? dia = null)
    {
        var caja = Caja.Crear(Empresa, $"Caja {cajero:N}"[..12], null);
        var sesion = CajaSesion.Abrir(Empresa, caja.Id, Cancun, cajero, 0m, dia ?? Dia, Ahora.AddHours(-4), null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(sesion);
        await db.SaveChangesAsync();
        return sesion;
    }

    private static Task<CobroMostradorResponse> CobrarAsync(
        FacturacionDbContext db, Guid cajero, Guid comprobanteId, params CobroFormaPagoInput[] formas) =>
        new RegistrarCobroMostradorHandler(
                db, new FakeEmpresaContext(Empresa), new FakeUserContext(cajero), new FakeClock(Ahora),
                Sucursales(), new FakeAlcanceCajaEvaluator(),
                new FakeIntegrationEventPublisher(), new FakeCatalogosSatReadPort())
            .Handle(new RegistrarCobroMostradorCommand(comprobanteId, formas), CancellationToken.None);

    private static ConsultaEstatusCfdiSimuladoAdapter Simulado(params string[] cancelados) =>
        ConsultaEstatusCfdiSimuladoAdapter.Con(cancelados, []);

    private static Task<MonitorCfdiResponse> MonitorAsync(
        FacturacionDbContext db, Guid? cajero = null, IConsultaEstatusCfdiPort? sat = null) =>
        new MonitorCfdiHandler(db, Usuarios(), Sucursales(), sat ?? Simulado(), new FakeClock(Ahora))
            .Handle(new MonitorCfdiQuery(Cancun, Dia, cajero), default);

    private static Task<ConciliacionFacturadoIngresadoResponse> ConciliarAsync(FacturacionDbContext db, Guid? cajero = null) =>
        new ConciliacionFacturadoIngresadoHandler(db, Usuarios(), Sucursales(), new FakeClock(Ahora))
            .Handle(new ConciliacionFacturadoIngresadoQuery(Cancun, Dia, cajero), default);

    [Fact]
    public async Task CA6_18_cierre_en_Cancun_muestra_timbradas_canceladas_y_sin_timbrar_y_cuadra()
    {
        using var db = NewDb();
        await AbrirSesionAsync(db, Ana);
        var cobrada = Factura(1000m, "FV-1");
        var cancelada = Factura(500m, "FV-2");
        var fallida = Factura(300m, "FV-3", timbrar: false);
        fallida.MarcarTimbradoEnProceso();
        fallida.MarcarTimbradoFallido("CFDI40147", "RFC del receptor no válido");
        var borrador = Factura(200m, "FV-4", timbrar: false);
        db.FacturasVenta.AddRange(cobrada, cancelada, fallida, borrador);
        foreach (var sinTimbre in new[] { fallida, borrador })
            db.Entry(sinTimbre).Property(c => c.CreatedAt).CurrentValue = Ahora.AddHours(-1);
        var nc = NotaCredito.CrearBonificacion(Empresa, "NC-1", 1, Cancun, null, Ana, Receptor(), Emisor(),
            "01", "MXN", null, 2026, 10, 1, cobrada.Id, 116m, null, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.NotasCredito.Add(nc);
        db.PedidosFacturables.Add(PedidoFacturable.ImportarDesdeAw(Empresa, "AW-77", Cancun, Guid.NewGuid(),
            "Cliente AW", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, null));
        await db.SaveChangesAsync();

        cancelada.MarcarCancelacionPendiente();
        cancelada.MarcarCancelado();
        await db.SaveChangesAsync();
        await CobrarAsync(db, Ana, cobrada.Id, new CobroFormaPagoInput("01", cobrada.Total - 116m, null));
        db.ChangeTracker.Clear();

        var monitor = await MonitorAsync(db);
        monitor.Resumen.Timbrados.Should().Be(2, "la factura cobrada y la NC");
        monitor.Resumen.Cancelados.Should().Be(1);
        monitor.Resumen.Fallidos.Should().Be(1);
        monitor.Resumen.SinTimbrar.Should().Be(1);
        monitor.Resumen.PedidosSinTimbrar.Should().Be(1);
        monitor.Comprobantes.Filas.Should().Contain(f => f.Folio == "FV-3" && f.Estado == "Fallido"
            && f.Detalle == "RFC del receptor no válido");
        monitor.Comprobantes.Filas.Should().Contain(f => f.Tipo == "Nota de crédito" && f.Folio == "NC-1");
        monitor.PedidosSinTimbrar.Filas.Single().Pedido.Should().Be("AW-77");

        var conciliacion = await ConciliarAsync(db);
        conciliacion.Resumen.Cuadra.Should().BeTrue();
        conciliacion.Resumen.Diferencia.Should().Be(0m);
        conciliacion.Resumen.Ingresado.Should().Be(cobrada.Total - 116m);
        conciliacion.Partidas.Filas.Should().ContainSingle(p => p.Situacion == ConciliacionFacturadoIngresadoHandler.Cuadra);
        conciliacion.Partidas.Filas.Should().NotContain(p => p.Comprobante.Contains("FV-2"), "una cancelada no es facturado");
    }

    [Fact]
    public async Task F6_7a_pedido_PUE_timbrado_sin_cobro_el_mismo_dia_es_diferencia()
    {
        using var db = NewDb();
        await AbrirSesionAsync(db, Ana);
        var sinCobro = Factura(1000m, "FV-10");
        db.FacturasVenta.Add(sinCobro);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var r = await ConciliarAsync(db);

        r.Resumen.Cuadra.Should().BeFalse();
        r.Resumen.Diferencia.Should().Be(sinCobro.Total);
        var partida = r.Partidas.Filas.Single();
        partida.Situacion.Should().Be(ConciliacionFacturadoIngresadoHandler.FacturadaSinCobro);
        partida.Diferencia.Should().Be(sinCobro.Total);
        partida.Cajero.Should().Be("Ana Cajera", "si nadie la cobró, responde quien la emitió");
    }

    [Fact]
    public async Task Credito_PPD_emitido_en_el_punto_aparece_como_informativo_y_no_mueve_la_diferencia()
    {
        using var db = NewDb();
        await AbrirSesionAsync(db, Ana);
        var credito = Factura(2000m, "FV-20", metodoPago: "PPD");
        db.FacturasVenta.Add(credito);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var r = await ConciliarAsync(db);

        r.Resumen.Cuadra.Should().BeTrue();
        r.Resumen.FacturasCredito.Should().Be(1);
        r.Resumen.FacturadoCredito.Should().Be(credito.Total);
        r.Partidas.Filas.Single().Situacion.Should().Be(ConciliacionFacturadoIngresadoHandler.Credito);
    }

    [Fact]
    public async Task Cobro_de_factura_de_otro_dia_y_transferencia_con_referencia()
    {
        using var db = NewDb();
        await AbrirSesionAsync(db, Ana);
        var ayer = Factura(100m, "FV-30", timbrada: Ahora.AddDays(-1));
        db.FacturasVenta.Add(ayer);
        await db.SaveChangesAsync();
        await CobrarAsync(db, Ana, ayer.Id, new CobroFormaPagoInput("03", ayer.Total, "SPEI-123"));
        db.ChangeTracker.Clear();

        var r = await ConciliarAsync(db);

        r.Resumen.Ingresado.Should().Be(ayer.Total);
        r.Resumen.IngresadoTransferencia.Should().Be(ayer.Total);
        r.Resumen.Diferencia.Should().Be(-ayer.Total);
        var partida = r.Partidas.Filas.Single();
        partida.Situacion.Should().Be(ConciliacionFacturadoIngresadoHandler.CobroOtroDia);
        partida.FormasPago.Should().Contain("SPEI-123");
    }

    [Fact]
    public async Task Por_cajero_cada_partida_queda_con_quien_la_cobro_o_la_emitio()
    {
        using var db = NewDb();
        await AbrirSesionAsync(db, Ana);
        await AbrirSesionAsync(db, Beto);
        var deAna = Factura(100m, "FV-40", emisor: Ana);
        var deBeto = Factura(200m, "FV-41", emisor: Beto);
        db.FacturasVenta.AddRange(deAna, deBeto);
        await db.SaveChangesAsync();
        await CobrarAsync(db, Ana, deAna.Id, new CobroFormaPagoInput("01", deAna.Total, null));
        db.ChangeTracker.Clear();

        var todos = await ConciliarAsync(db);
        todos.PorCajero.Filas.Should().HaveCount(2);
        todos.PorCajero.Filas.Single(c => c.Cajero == "Ana Cajera").Estado.Should().Be("Cuadra");
        todos.PorCajero.Filas.Single(c => c.Cajero == "Beto Cajero").Diferencia.Should().Be(deBeto.Total);

        var soloBeto = await ConciliarAsync(db, Beto);
        soloBeto.Partidas.Filas.Should().ContainSingle(p => p.Comprobante.Contains("FV-41"));
        soloBeto.Cajero.Should().Be("Beto Cajero");

        var monitorAna = await MonitorAsync(db, Ana);
        monitorAna.Comprobantes.Filas.Should().ContainSingle(f => f.Folio == "FV-40");
    }

    [Fact]
    public async Task El_dia_se_corta_a_medianoche_de_Cancun()
    {
        using var db = NewDb();
        // 23:30 del 23 en Cancún (04:30Z del 24) entra; 00:30 del 24 (05:30Z) no.
        db.FacturasVenta.Add(Factura(100m, "FV-50", timbrada: new DateTimeOffset(2026, 10, 24, 4, 30, 0, TimeSpan.Zero)));
        db.FacturasVenta.Add(Factura(100m, "FV-51", timbrada: new DateTimeOffset(2026, 10, 24, 5, 30, 0, TimeSpan.Zero)));
        // 23:30 del 22 en Cancún no entra.
        db.FacturasVenta.Add(Factura(100m, "FV-52", timbrada: new DateTimeOffset(2026, 10, 23, 4, 30, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var monitor = await MonitorAsync(db);

        monitor.Comprobantes.Filas.Select(f => f.Folio).Should().Equal("FV-50");
        monitor.Comprobantes.Filas.Single().Fecha.Should().Be("23/10/2026 23:30");
    }

    [Fact]
    public async Task Verificar_en_SAT_guarda_el_estatus_y_el_monitor_marca_la_discrepancia()
    {
        using var db = NewDb();
        var vigente = Factura(100m, "FV-60");
        var canceladaEnSat = Factura(100m, "FV-61");
        db.FacturasVenta.AddRange(vigente, canceladaEnSat);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var sat = Simulado(canceladaEnSat.Uuid!);

        var antes = await MonitorAsync(db, sat: sat);
        antes.Resumen.SinVerificarSat.Should().Be(2);
        antes.Comprobantes.Filas.Should().OnlyContain(f => f.EstatusSat == "Sin verificar" && !f.Discrepancia);

        var r = await new VerificarEstatusSatHandler(db, Sucursales(), sat, new FakeClock(Ahora),
                NullLogger<VerificarEstatusSatHandler>.Instance)
            .Handle(new VerificarEstatusSatCommand(Cancun, Dia), default);
        r.Consultados.Should().Be(2);
        r.Vigentes.Should().Be(1);
        r.Cancelados.Should().Be(1);
        r.Fuente.Should().Contain("Simulado");
        db.ChangeTracker.Clear();

        var despues = await MonitorAsync(db, sat: sat);
        despues.Resumen.SinVerificarSat.Should().Be(0);
        despues.Resumen.Discrepancias.Should().Be(1);
        despues.Comprobantes.Filas.Single(f => f.Folio == "FV-61").Should().Match<MonitorCfdiFila>(f =>
            f.EstatusSat == "Cancelado" && f.Discrepancia && f.EstatusSatConsultadoEn == "23/10/2026 13:00");
        despues.FuenteSat.Should().Contain("Simulado");
    }

    [Fact]
    public async Task Cancelacion_aceptada_hoy_de_un_CFDI_de_otro_dia_aparece_en_el_monitor()
    {
        using var db = NewDb();
        var anterior = Factura(100m, "FV-70", timbrada: Ahora.AddDays(-3));
        db.FacturasVenta.Add(anterior);
        var solicitud = SolicitudCancelacion.Crear(Empresa, anterior.Id, "02", null, Ahora.AddDays(-1));
        solicitud.MarcarEnProceso(null);
        solicitud.MarcarAceptada("Cancelado", Ahora);
        db.SolicitudesCancelacion.Add(solicitud);
        anterior.MarcarCancelacionPendiente();
        anterior.MarcarCancelado();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var monitor = await MonitorAsync(db);

        monitor.Resumen.Cancelados.Should().Be(1);
        monitor.Comprobantes.Filas.Single().Folio.Should().Be("FV-70");
    }

    [Fact]
    public async Task Sin_asociacion_a_la_sucursal_no_responde()
    {
        using var db = NewDb();
        var query = new MonitorCfdiQuery(Cancun, Dia) { SucursalesPermitidas = [Guid.NewGuid()] };

        var act = () => new MonitorCfdiHandler(db, Usuarios(), Sucursales(), Simulado(), new FakeClock(Ahora))
            .Handle(query, default);

        await act.Should().ThrowAsync<ForbiddenException>();
        var conciliacion = () => new ConciliacionFacturadoIngresadoHandler(db, Usuarios(), Sucursales(), new FakeClock(Ahora))
            .Handle(new ConciliacionFacturadoIngresadoQuery(Cancun, Dia) { SucursalesPermitidas = [] }, default);
        await conciliacion.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task El_adaptador_simulado_se_resuelve_por_DI_con_UUID_forzados_desde_configuracion()
    {
        var uuid = Guid.NewGuid().ToString();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"{ConsultaEstatusSatConfig.Seccion}:UuidsCancelados"] = $" {uuid} , otro" })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddScoped<IConsultaEstatusCfdiPort, ConsultaEstatusCfdiSimuladoAdapter>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IConsultaEstatusCfdiPort>();

        var r = await port.ConsultarAsync(new ConsultaEstatusCfdi(Empresa, uuid, "BBB010101BBB", "AAA010101AAA", 1m, null, EstadoTimbrado.Timbrado), default);

        r.EstatusSat.Should().Be(EstatusSat.Cancelado);
    }

    [Fact]
    public async Task Adaptador_real_normaliza_la_respuesta_de_FiscalAPI()
    {
        var adapter = new ConsultaEstatusCfdiFiscalApiAdapter(new FakeFiscalApiClient());

        var r = await adapter.ConsultarAsync(new ConsultaEstatusCfdi(Empresa, Guid.NewGuid().ToString(),
            "BBB010101BBB", "AAA010101AAA", 116m, "12345678", EstadoTimbrado.Timbrado), default);

        r.EstatusSat.Should().Be(EstatusSat.Vigente);
        adapter.Fuente.Should().Contain("SAT");
    }
}
