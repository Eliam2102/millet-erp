using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.PublicAdapters;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Repp;

/// <summary>
/// H-U49: fiscalmente no se emite REP de una factura PUE. Una confirmación de pago sobre facturas PUE no crea pendiente de REP,
/// una mixta lo crea solo por lo PPD, y un pendiente viejo que solo relaciona PUE no bloquea el estado de cuenta. Datos ficticios.
/// </summary>
public sealed class ReppPendientePueTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();

    private static FacturacionDbContext Db() => new(new DbContextOptionsBuilder<FacturacionDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Empresa));

    private static FacturaVenta Factura(FacturacionDbContext db, string metodo, decimal valor)
    {
        var f = FacturaVenta.CrearBorrador(Empresa, "PRUEBA-" + metodo, 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio", "601", "97000"), metodo, "03", "MXN",
            null, 2026, 11, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Servicio ficticio", "E48", 1, valor, 0, "01", null, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(f);
        return f;
    }

    private static PagoClienteConfirmadoPayload Pago(decimal monto, params PagoClienteFacturaAplicadaPayload[] facturas) =>
        new(Empresa, Ahora, Guid.NewGuid(), Cliente, Guid.NewGuid(), Guid.NewGuid(), monto, "MXN", new(2026, 11, 12), "Prueba", facturas);

    [Fact]
    public async Task Confirmacion_solo_PUE_no_crea_pendiente_de_REP()
    {
        using var db = Db();
        var pue = Factura(db, "PUE", 1100m);
        await db.SaveChangesAsync();

        await new EmitirReppDesdePagoConfirmadoHandler(db, new FakeClock(Ahora))
            .Handle(new(Guid.NewGuid(), Pago(pue.Total, new(pue.Id, pue.Total))), default);

        (await db.ReppPendientes.CountAsync()).Should().Be(0);
        (await db.EventosProcesados.CountAsync()).Should().Be(1, "el evento queda procesado aunque no haya REP");
    }

    [Fact]
    public async Task Confirmacion_mixta_relaciona_solo_lo_PPD()
    {
        using var db = Db();
        var pue = Factura(db, "PUE", 300m);
        var ppd = Factura(db, "PPD", 700m);
        await db.SaveChangesAsync();

        await new EmitirReppDesdePagoConfirmadoHandler(db, new FakeClock(Ahora))
            .Handle(new(Guid.NewGuid(), Pago(pue.Total + 700m, new(pue.Id, pue.Total), new(ppd.Id, 700m))), default);

        var pendiente = await db.ReppPendientes.Include(p => p.Facturas).SingleAsync();
        pendiente.Monto.Should().Be(700m);
        pendiente.Facturas.Should().ContainSingle().Which.FacturaVentaId.Should().Be(ppd.Id);
    }

    [Fact]
    public async Task Pendiente_viejo_solo_PUE_no_bloquea_el_estado_de_cuenta()
    {
        using var db = Db();
        var pue = Factura(db, "PUE", 1100m);
        var ppd = Factura(db, "PPD", 500m);
        db.ReppPendientes.Add(new ReppPendiente(Empresa, Cliente, Guid.NewGuid(), Guid.NewGuid(), null, pue.Total, "MXN",
            new DateOnly(2026, 11, 10), "Pendiente previo", [new RelacionRepp(pue.Id, pue.Total)]));
        await db.SaveChangesAsync();

        var adapter = new ReppPendientesReadAdapter(db);
        var soloPue = await adapter.ConsultarAsync(Cliente, default);
        soloPue.TienePendientes.Should().BeFalse();
        soloPue.Pendientes.Should().BeEmpty();

        // Un pendiente PPD sí sigue bloqueando.
        db.ReppPendientes.Add(new ReppPendiente(Empresa, Cliente, Guid.NewGuid(), Guid.NewGuid(), null, 500m, "MXN",
            new DateOnly(2026, 11, 11), "Pendiente PPD", [new RelacionRepp(ppd.Id, 500m)]));
        await db.SaveChangesAsync();
        (await adapter.ConsultarAsync(Cliente, default)).TienePendientes.Should().BeTrue();
    }
}
