using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// CAJAS-PR7: batch de liquidación de ruta (`[Decisión 12-7]`, todo-o-nada),
/// query de comprobantes cobrables y lectura de ajustes de caja (`[12-C]`).
/// </summary>
public sealed class LiquidacionRutaTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cajero = Guid.NewGuid();
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 7, 11, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(Empresa));

    private static DatosFiscalesReceptor Receptor(string nombre = "Cliente") =>
        new("AAA010101AAA", nombre, "601", "97000", "G03", "MEX", false);

    private static DatosFiscalesEmisor Emisor() => new("BBB010101BBB", "Millet", "601", "76120");

    private static FacturaVenta Factura(decimal valor, string folio, string metodoPago = "PUE", bool timbrar = true)
    {
        var fv = FacturaVenta.CrearBorrador(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
            metodoPago, "01", "MXN", null, 2026, 7, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        if (timbrar)
        {
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        }
        return fv;
    }

    private static ReciboPago Repp(decimal importe, string folio)
    {
        var repp = ReciboPago.CrearBorrador(Empresa, folio, 1, SucursalId, null, null, Receptor(), Emisor(),
            2026, 7, Ahora, "MXN", 1);
        repp.EstablecerImporteTotalPago(importe);
        repp.MarcarTimbradoEnProceso();
        repp.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return repp;
    }

    private static async Task<(Caja Caja, CajaSesion Sesion)> SembrarSesionAsync(FacturacionDbContext db)
    {
        var caja = Caja.Crear(Empresa, "Caja Reparto", null);
        var sesion = CajaSesion.Abrir(
            Empresa, caja.Id, SucursalId, Cajero, 500m, new DateOnly(2026, 7, 11), Ahora.AddHours(-2), null);
        db.Cajas.Add(caja);
        db.CajaSesiones.Add(sesion);
        await db.SaveChangesAsync();
        return (caja, sesion);
    }

    private static LiquidarRutaHandler Handler(
        FacturacionDbContext db, IAlcanceCajaEvaluator? alcance = null) =>
        new(db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), alcance ?? new FakeAlcanceCajaEvaluator(),
            new FakeIntegrationEventPublisher());

    private static LiquidacionRutaCobroInput Efectivo(Guid comprobanteId, decimal importe) =>
        new(comprobanteId, [new CobroFormaPagoInput("01", importe)]);

    // ---- Batch ----

    [Fact]
    public async Task LiquidarRuta_registra_todos_los_cobros_con_origen_ruta()
    {
        using var db = NewDb();
        var (caja, sesion) = await SembrarSesionAsync(db);
        var f1 = Factura(400m, "F-1");
        var f2 = Factura(600m, "F-2");
        var repp = Repp(750m, "P-1");
        db.FacturasVenta.AddRange(f1, f2);
        db.RecibosPago.Add(repp);
        await db.SaveChangesAsync();

        var response = await Handler(db).Handle(new LiquidarRutaCommand(
            [Efectivo(f1.Id, 400m), Efectivo(f2.Id, 600m), Efectivo(repp.Id, 750m)]),
            CancellationToken.None);

        response.CajaSesionId.Should().Be(sesion.Id);
        response.Total.Should().Be(1750m);
        response.Cobros.Should().HaveCount(3);

        var cobros = await db.CobrosMostrador.ToListAsync();
        cobros.Should().HaveCount(3);
        cobros.Should().OnlyContain(c => c.Origen == OrigenCobroMostrador.LiquidacionRuta);
        cobros.Should().OnlyContain(c => c.CajaSesionId == sesion.Id);

        (await db.CajaMovimientos.CountAsync(m => m.Tipo == TipoCajaMovimiento.CobroCliente))
            .Should().Be(3);
        f1.CajaId.Should().Be(caja.Id);
        f2.CajaId.Should().Be(caja.Id);
    }

    [Fact]
    public async Task LiquidarRuta_es_atomica_si_un_cobro_falla_no_se_registra_ninguno()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var f1 = Factura(400m, "F-1");
        var f2 = Factura(600m, "F-2");
        db.FacturasVenta.AddRange(f1, f2);
        await db.SaveChangesAsync();

        // El segundo cobro no cuadra con el total → nada se persiste.
        var act = () => Handler(db).Handle(new LiquidarRutaCommand(
            [Efectivo(f1.Id, 400m), Efectivo(f2.Id, 599m)]), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "COBRO_TOTAL_NO_COINCIDE");
        (await db.CobrosMostrador.CountAsync()).Should().Be(0);
        (await db.CajaMovimientos.CountAsync(m => m.Tipo == TipoCajaMovimiento.CobroCliente)).Should().Be(0);
    }

    [Fact]
    public async Task LiquidarRuta_falla_si_algun_comprobante_esta_fuera_de_alcance()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var f1 = Factura(400m, "F-1");
        db.FacturasVenta.Add(f1);
        await db.SaveChangesAsync();

        var act = () => Handler(db, new FakeAlcanceCajaEvaluator(AlcanceCajas.Ninguno()))
            .Handle(new LiquidarRutaCommand([Efectivo(f1.Id, 400m)]), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
        (await db.CobrosMostrador.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void LiquidarRuta_validator_rechaza_duplicados_y_vacios()
    {
        var validator = new LiquidarRutaValidator();
        var id = Guid.NewGuid();

        validator.Validate(new LiquidarRutaCommand([])).IsValid.Should().BeFalse();
        validator.Validate(new LiquidarRutaCommand([Efectivo(id, 1m), Efectivo(id, 1m)]))
            .IsValid.Should().BeFalse();
        validator.Validate(new LiquidarRutaCommand([new LiquidacionRutaCobroInput(id, [])]))
            .IsValid.Should().BeFalse();
        validator.Validate(new LiquidarRutaCommand([Efectivo(id, 100m)])).IsValid.Should().BeTrue();
    }

    // ---- Cobrables ----

    [Fact]
    public async Task Cobrables_lista_timbrados_sin_cobro_y_excluye_ppd_borradores_y_cobrados()
    {
        using var db = NewDb();
        await SembrarSesionAsync(db);
        var cobrable = Factura(400m, "F-1");
        var ppd = Factura(500m, "F-2", metodoPago: "PPD");
        var borrador = Factura(600m, "F-3", timbrar: false);
        var cobrada = Factura(700m, "F-4");
        var repp = Repp(750m, "P-1");
        db.FacturasVenta.AddRange(cobrable, ppd, borrador, cobrada);
        db.RecibosPago.Add(repp);
        await db.SaveChangesAsync();

        // Cobra F-4 para que salga de la lista.
        var registrar = new RegistrarCobroMostradorHandler(
            db, new FakeEmpresaContext(Empresa), new FakeUserContext(Cajero), new FakeClock(Ahora),
            new FakeSucursalesReadPort(), new FakeAlcanceCajaEvaluator(), new FakeIntegrationEventPublisher());
        await registrar.Handle(new RegistrarCobroMostradorCommand(
            cobrada.Id, [new CobroFormaPagoInput("01", 700m)]), CancellationToken.None);

        var handler = new ListarComprobantesCobrablesHandler(db, new FakeAlcanceCajaEvaluator());
        var items = await handler.Handle(new ListarComprobantesCobrablesQuery(), CancellationToken.None);

        items.Select(i => i.Folio).Should().BeEquivalentTo(["F-1", "P-1"]);
        items.Single(i => i.Folio == "P-1").Total.Should().Be(750m); // ImporteTotalPago
        items.Single(i => i.Folio == "F-1").Total.Should().Be(400m);

        // Con alcance Ninguno no ve nada.
        var vacio = await new ListarComprobantesCobrablesHandler(
                db, new FakeAlcanceCajaEvaluator(AlcanceCajas.Ninguno()))
            .Handle(new ListarComprobantesCobrablesQuery(), CancellationToken.None);
        vacio.Should().BeEmpty();
    }

    // ---- Ajustes de caja ----

    [Fact]
    public async Task AjustesCaja_lista_pendientes_y_exige_permiso_de_lectura()
    {
        using var db = NewDb();
        var (caja, sesion) = await SembrarSesionAsync(db);
        var cobroId = Guid.NewGuid();
        var pendiente = CajaAjustePendiente.Crear(Empresa, caja.Id, cobroId, -300m, "01", "Cancelación", Cajero);
        var aplicado = CajaAjustePendiente.Crear(Empresa, caja.Id, cobroId, -50m, "01", "Otro", Cajero);
        aplicado.MarcarAplicado(sesion.Id);
        db.CajaAjustesPendientes.AddRange(pendiente, aplicado);
        await db.SaveChangesAsync();

        var handler = new ListarAjustesCajaHandler(
            db, new FakeCurrentUserPermissions("facturacion.caja.operar"));

        var pendientes = await handler.Handle(new ListarAjustesCajaQuery(caja.Id), CancellationToken.None);
        pendientes.Should().ContainSingle().Which.Importe.Should().Be(-300m);
        pendientes[0].AplicadoEnSesionId.Should().BeNull();

        var todos = await handler.Handle(
            new ListarAjustesCajaQuery(caja.Id, IncluirAplicados: true), CancellationToken.None);
        todos.Should().HaveCount(2);

        var sinPermiso = () => new ListarAjustesCajaHandler(db, new FakeCurrentUserPermissions())
            .Handle(new ListarAjustesCajaQuery(caja.Id), CancellationToken.None);
        await sinPermiso.Should().ThrowAsync<ForbiddenException>()
            .Where(e => e.Code == "CAJA_PERMISO_INSUFICIENTE");
    }
}
