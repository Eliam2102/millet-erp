using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Activos.Queries;
using Millet.Facturacion.Application.Anticipos.Queries;
using Millet.Facturacion.Application.CartaPorte.Queries;
using Millet.Facturacion.Application.NotasCredito.Queries;
using Millet.Facturacion.Application.Reportes.ControlAnticipos;
using Millet.Facturacion.Application.Repp.Queries;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Activos;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using DominioCp = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.UnitTests.Lectura;

/// <summary>B9/B11/B12/B13 — bandejas y detalles de lectura (FE-F4/F6/F8/F9).</summary>
public sealed class BandejasLecturaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static DatosFiscalesReceptor Receptor() => new("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    private static FacturaVenta FacturaTimbrada(Guid empresaId, string folio, string uuid)
    {
        var fv = FacturaVenta.CrearBorrador(empresaId, folio, 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PPD", "99", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 1000m, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(uuid, null, null, null, Ahora, null, null);
        return fv;
    }

    // ---- B9: REPP ----

    [Fact]
    public async Task Repp_bandeja_y_detalle_con_facturas_cubiertas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = FacturaTimbrada(empresaId, "F-1", "U-FACT");
        var repp = ReciboPago.CrearBorrador(empresaId, "P-1", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), 2026, 5, Ahora, "MXN");
        repp.AgregarFacturaPagada(factura.Id, "U-FACT", 1, "MXN", 400m, 1000m, "03", null, null, null, null, null, "01", 1000m, []);
        repp.EstablecerImporteTotalPago(400m);
        repp.MarcarTimbradoEnProceso();
        repp.MarcarTimbrado("U-REPP", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(factura);
        db.RecibosPago.Add(repp);
        await db.SaveChangesAsync();

        var alcance = new FakeAlcanceCajaEvaluator();
        var bandeja = await new BandejaReppHandler(db, alcance).Handle(new BandejaReppQuery(null, 0, 50), CancellationToken.None);
        bandeja.Items.Should().ContainSingle().Which.ImporteTotalPago.Should().Be(400m);

        var detalle = await new ReppDetalleHandler(db, alcance).Handle(new ReppDetalleQuery(repp.Id), CancellationToken.None);
        detalle.FacturasCubiertas.Should().ContainSingle();
        detalle.FacturasCubiertas[0].Folio.Should().Be("F-1"); // enriquecido
        detalle.FacturasCubiertas[0].SaldoInsoluto.Should().Be(600m);
    }

    // ---- B11: Carta Porte ----

    [Fact]
    public async Task CartaPorte_bandeja_y_detalle_con_vehiculo_operador_mercancias()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var v = DominioCp.Vehiculo.Crear(empresaId, "ABC-123", "VL", 2022);
        var o = DominioCp.Operador.Crear(empresaId, "OPER010101AAA", "Juan", "LIC-1");
        var cp = DominioCp.CartaPorte.CrearBorrador(empresaId, TipoComprobante.Traslado, "CP-1", 1, Guid.NewGuid(),
            null, null, Receptor(), new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "MXN", 2026, 5, "Conkal", "Cancún", 300m,
            v.Id, o.Id, null, null, Ahora, Ahora.AddHours(4));
        cp.AgregarMercancia("Vidrio", "43211503", "KGM", 10m, 500m, false);
        cp.MarcarTimbradoEnProceso();
        cp.MarcarTimbrado("U-CP", null, null, null, Ahora, null, null);
        db.Vehiculos.Add(v); db.Operadores.Add(o); db.CartasPorte.Add(cp);
        await db.SaveChangesAsync();

        var bandeja = await new BandejaCartaPorteHandler(db).Handle(new BandejaCartaPorteQuery(null, 0, 50), CancellationToken.None);
        bandeja.Should().ContainSingle();
        bandeja[0].Tipo.Should().Be("T");
        bandeja[0].Tramo.Should().Be("Conkal → Cancún");

        var detalle = await new CartaPorteDetalleHandler(db).Handle(new CartaPorteDetalleQuery(cp.Id), CancellationToken.None);
        detalle.Vehiculo!.Placa.Should().Be("ABC-123");
        detalle.Operador!.Nombre.Should().Be("Juan");
        detalle.Mercancias.Should().ContainSingle();
    }

    // ---- B13: Activos ----

    [Fact]
    public async Task Activos_bandeja_lista_las_autorizadas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.AutorizacionesVentaActivo.Add(AutorizacionVentaActivo.Crear(empresaId, "AF-1", "Torno", 100000m, 60000m, false, 50000m, Guid.NewGuid(), Ahora));
        await db.SaveChangesAsync();

        var items = await new BandejaAutorizacionesActivoHandler(db).Handle(new BandejaAutorizacionesActivoQuery(null), CancellationToken.None);

        items.Should().ContainSingle();
        items[0].UtilidadOPerdida.Should().Be(10000m);
        items[0].Estado.Should().Be("Autorizada");
    }

    // ---- CAJAS-PR2: bandeja/detalle de facturas de anticipo y NC ----

    [Fact]
    public async Task FacturasAnticipo_bandeja_y_detalle()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var anticipoId = Guid.CreateVersion7();
        var fa = FacturaAnticipo.CrearBorrador(empresaId, "FANT-1", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "03", "MXN", null, 2026, 5,
            TipoAnticipo.ClientesMxp, null, anticipoId, 1000m, 0.16m, canalVentaId: 2);
        db.FacturasAnticipo.Add(fa);
        await db.SaveChangesAsync();

        var alcance = new FakeAlcanceCajaEvaluator();
        var bandeja = await new BandejaFacturasAnticipoHandler(db, alcance)
            .Handle(new BandejaFacturasAnticipoQuery(null, 0, 50), CancellationToken.None);
        bandeja.Items.Should().ContainSingle().Which.Total.Should().Be(1160m);

        var detalle = await new FacturaAnticipoDetalleHandler(db, alcance)
            .Handle(new FacturaAnticipoDetalleQuery(fa.Id), CancellationToken.None);
        detalle.AnticipoId.Should().Be(anticipoId);
        detalle.TipoAnticipo.Should().Be("ClientesMxp");

        var noExiste = () => new FacturaAnticipoDetalleHandler(db, alcance)
            .Handle(new FacturaAnticipoDetalleQuery(Guid.NewGuid()), CancellationToken.None);
        await noExiste.Should().ThrowAsync<Millet.SharedKernel.Application.Exceptions.EntityNotFoundException>();
    }

    [Fact]
    public async Task NotasCredito_bandeja_y_detalle_con_relaciones()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var nc = NotaCredito.CrearBonificacion(empresaId, "NC-1", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "03", "MXN", null, 2026, 5,
            canalVentaId: 2, facturaRelacionadaId: Guid.NewGuid(), montoTotal: 116m, tasaIva: 0.16m, descripcion: null);
        nc.AgregarRelacion("U-FACT", "01");
        db.NotasCredito.Add(nc);
        await db.SaveChangesAsync();

        var alcance = new FakeAlcanceCajaEvaluator();
        var bandeja = await new BandejaNotasCreditoHandler(db, alcance)
            .Handle(new BandejaNotasCreditoQuery(null, 0, 50), CancellationToken.None);
        bandeja.Items.Should().ContainSingle().Which.Motivo.Should().Be("Bonificacion");

        var detalle = await new NotaCreditoDetalleHandler(db, alcance)
            .Handle(new NotaCreditoDetalleQuery(nc.Id), CancellationToken.None);
        detalle.Total.Should().Be(116m);
        detalle.Relaciones.Should().ContainSingle().Which.TipoRelacion.Should().Be("01");
    }

    // ---- B12: anticipos detallada ----

    [Fact]
    public async Task ControlAnticipos_detallada_arma_estado_de_cuenta()
    {
        var empresaId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var anticipoId = Guid.CreateVersion7();
        var fa = FacturaAnticipo.CrearBorrador(empresaId, "FANT-1", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "03", "MXN", null, 2026, 5, TipoAnticipo.ClientesMxp, null, anticipoId, 1000m, 0.16m);
        fa.MarcarTimbradoEnProceso();
        fa.MarcarTimbrado("U-FANT", null, null, null, Ahora, null, null);
        var anticipo = Anticipo.Crear(empresaId, clienteId, "AAA010101AAA", TipoAnticipo.ClientesMxp, "MXN", 1160m, fa.Id, id: anticipoId);
        anticipo.Vincular(Guid.NewGuid(), 400m, Ahora);
        db.FacturasAnticipo.Add(fa);
        db.Anticipos.Add(anticipo);
        await db.SaveChangesAsync();

        var detalle = await new ControlAnticiposDetalladaHandler(db, new FakeClock(Ahora))
            .Handle(new ControlAnticiposDetalladaQuery(clienteId), CancellationToken.None);

        detalle.Anticipos.Should().ContainSingle();
        detalle.Anticipos[0].Folio.Should().Be("FANT-1");
        detalle.Anticipos[0].Vinculaciones.Should().ContainSingle().Which.Importe.Should().Be(400m);
        detalle.TotalSaldo.Should().Be(1160m);
    }
}
