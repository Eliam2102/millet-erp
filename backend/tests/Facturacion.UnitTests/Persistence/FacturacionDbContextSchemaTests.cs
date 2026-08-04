using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Persistence;

public sealed class FacturacionDbContextSchemaTests
{
    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    [Fact]
    public void El_modelo_se_construye_con_la_jerarquia_TPT()
    {
        using var db = NewDb(Guid.NewGuid());

        // Forzar la finalización del modelo: si el query filter del
        // BaseDbContext se aplicara al tipo derivado FacturaVenta (en vez de
        // sólo a la raíz Comprobante), EF Core lanzaría aquí.
        var act = () => _ = db.Model;

        act.Should().NotThrow();
        db.Model.FindEntityType(typeof(Comprobante)).Should().NotBeNull();
        db.Model.FindEntityType(typeof(FacturaVenta)).Should().NotBeNull();
        db.Model.FindEntityType(typeof(FacturaVentaLinea)).Should().NotBeNull();
    }

    [Fact]
    public async Task Persiste_y_recupera_una_factura_de_venta_con_lineas()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var f = FacturaVenta.CrearBorrador(
            empresaId: empresaId,
            folio: "FA-000007",
            folioNumero: 7,
            sucursalId: Guid.NewGuid(),
            cajaId: null,
            usuarioEmisorId: null,
            receptor: new DatosFiscalesReceptor("XAXX010101000", "Público", "616", "97000", "S01", "MEX", true),
            emisor: new DatosFiscalesEmisor("AAA010101AAA", "Millet", "601", "76120"),
            metodoPago: "PUE",
            formaPago: "01",
            moneda: "MXN",
            tipoCambio: null,
            periodoAnio: 2026,
            periodoMes: 5,
            canalVentaId: (short)1,
            comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
            pedidoFacturableId: null,
            obraId: null,
            obraNombre: null,
            facturaAgrupada: false);
        f.AgregarLinea(null, "01010101", "Producto", "H87", 1m, 50m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();

        db.FacturasVenta.Add(f);
        await db.SaveChangesAsync();

        var loaded = await db.FacturasVenta.Include(x => x.Lineas).SingleAsync();
        loaded.Folio.Should().Be("FA-000007");
        loaded.Total.Should().Be(58m);
        loaded.Lineas.Should().HaveCount(1);
        loaded.Lineas.Single().ClaveProdServSat.Should().Be("01010101");
    }
}
