using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Reportes.CfdisPorObra;
using Millet.Facturacion.Application.Reportes.EstadosFacturasAnticipo;
using Millet.Facturacion.Application.Reportes.LiquidacionCaja;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Reportes;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Reportes;

public sealed class ReportesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static FacturaVenta FacturaTimbrada(Guid empresaId, string formaPago, decimal valor, long? obraId = null)
    {
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-" + Guid.NewGuid().ToString("N")[..6], 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", formaPago, "MXN", null, 2026, 5,
            (short)7, ComportamientoFiscal.MostradorInmediato, null, obraId, obraId is null ? null : "Obra Norte", false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, valor, 0m, "02", 0m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return fv;
    }

    [Fact]
    public async Task LiquidacionCaja_lista_sesiones_cerradas_con_diferencias()
    {
        // [Decisión 12-11] (CAJAS-PR4): el reporte se redefine sobre las
        // sesiones de efectivo — una fila por sesión CERRADA en el rango.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var caja = Millet.Facturacion.Domain.Cajas.Caja.Crear(empresaId, "Caja Mostrador", null);
        db.Cajas.Add(caja);

        var sesion = Millet.Facturacion.Domain.Cajas.CajaSesion.Abrir(
            empresaId, caja.Id, Guid.NewGuid(), Guid.NewGuid(), 500m,
            new DateOnly(2026, 5, 30), Ahora.AddHours(-8), null);
        sesion.IniciarArqueo(new Dictionary<string, decimal> { ["01"] = 900m, ["03"] = 2000m });
        sesion.Cerrar(880m, "faltante", cierreExtemporaneo: false, Ahora.AddMinutes(-5));
        db.CajaSesiones.Add(sesion);

        // Sesión aún abierta: no aparece en la liquidación.
        db.CajaSesiones.Add(Millet.Facturacion.Domain.Cajas.CajaSesion.Abrir(
            empresaId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m,
            new DateOnly(2026, 5, 30), Ahora.AddHours(-1), null));
        await db.SaveChangesAsync();

        var r = await new LiquidacionCajaHandler(db, new FakeClock(Ahora))
            .Handle(new LiquidacionCajaQuery(null, Ahora.AddHours(-1), Ahora.AddHours(1)), CancellationToken.None);

        var fila = r.Filas.Should().ContainSingle().Subject;
        fila.Caja.Should().Be("Caja Mostrador");
        fila.EfectivoTeorico.Should().Be(900m);
        fila.EfectivoDeclarado.Should().Be(880m);
        fila.Diferencia.Should().Be(-20m);
        fila.CierreExtemporaneo.Should().Be("No");
        r.Totales!["diferencia"].Should().Be(-20m);
    }

    [Fact]
    public async Task EstadosFacturasAnticipo_lista_con_saldos()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var anticipoId = Guid.CreateVersion7();
        var fa = FacturaAnticipo.CrearBorrador(empresaId, "FANT-1", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente Maquila", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "03", "MXN", null, 2026, 5, TipoAnticipo.ClientesMxp, null, anticipoId, 1000m, 0.16m);
        fa.MarcarTimbradoEnProceso();
        fa.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        var anticipo = Anticipo.Crear(empresaId, Guid.NewGuid(), "AAA010101AAA", TipoAnticipo.ClientesMxp, "MXN", 1160m, fa.Id, id: anticipoId);
        db.FacturasAnticipo.Add(fa);
        db.Anticipos.Add(anticipo);
        await db.SaveChangesAsync();

        var r = await new EstadosFacturasAnticipoHandler(db, new FakeClock(Ahora))
            .Handle(new EstadosFacturasAnticipoQuery(null, null), CancellationToken.None);

        r.Filas.Should().ContainSingle();
        r.Filas[0].Saldo.Should().Be(1160m);
        r.Filas[0].Timbrado.Should().BeTrue();
        r.Totales!["saldo"].Should().Be(1160m);
    }

    [Fact]
    public async Task CfdisPorObra_devuelve_los_de_la_obra()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        db.FacturasVenta.Add(FacturaTimbrada(empresaId, "01", 1000m, obraId: 42));
        db.FacturasVenta.Add(FacturaTimbrada(empresaId, "01", 500m, obraId: 99));
        await db.SaveChangesAsync();

        var handler = new CfdisPorObraHandler(new FacturacionCfdiReadAdapter(db), new FakeClock(Ahora));
        var r = await handler.Handle(new CfdisPorObraQuery(42), CancellationToken.None);

        r.Filas.Should().ContainSingle();
        r.Filas[0].Total.Should().Be(1000m);
        r.Totales!["total"].Should().Be(1000m);
    }
}
