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

        var cajero = Guid.NewGuid();
        var sesion = Millet.Facturacion.Domain.Cajas.CajaSesion.Abrir(
            empresaId, caja.Id, Guid.NewGuid(), cajero, 500m,
            new DateOnly(2026, 5, 30), Ahora.AddHours(-8), null);
        sesion.IniciarArqueo(new Dictionary<string, decimal> { ["01"] = 900m, ["03"] = 2000m });
        sesion.Cerrar(880m, "faltante", cierreExtemporaneo: false, Ahora.AddMinutes(-5));
        db.CajaSesiones.Add(sesion);

        // Sesión aún abierta: no aparece en la liquidación.
        db.CajaSesiones.Add(Millet.Facturacion.Domain.Cajas.CajaSesion.Abrir(
            empresaId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 100m,
            new DateOnly(2026, 5, 30), Ahora.AddHours(-1), null));
        await db.SaveChangesAsync();

        var r = await new LiquidacionCajaHandler(db, new FakeClock(Ahora),
                new FakeUsuariosReadPort(new Dictionary<Guid, string> { [cajero] = "Ana Cajera" }))
            .Handle(new LiquidacionCajaQuery(null, Ahora.AddHours(-1), Ahora.AddHours(1)), CancellationToken.None);

        var fila = r.Filas.Should().ContainSingle().Subject;
        fila.Caja.Should().Be("Caja Mostrador");
        // FIX-12: corte por cajero (Manual de Sucursales §2.4) y enlace a su sesión.
        fila.Cajero.Should().Be("Ana Cajera");
        fila.SesionId.Should().Be(sesion.Id);
        fila.EfectivoTeorico.Should().Be(900m);
        fila.EfectivoDeclarado.Should().Be(880m);
        fila.Diferencia.Should().Be(-20m);
        fila.CierreExtemporaneo.Should().Be("No");
        r.Totales!["diferencia"].Should().Be(-20m);
    }

    [Fact]
    public async Task LiquidacionCaja_filtra_por_dia_de_operacion_local_no_por_hora_UTC()
    {
        // FIX-12: la pantalla pide «hasta hoy» con una fecha sin hora. Un cierre
        // a las 19:30 de Mérida (01:30 UTC del día siguiente) pertenece al día de
        // operación local y debe aparecer; una sesión del día anterior, no.
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var caja = Millet.Facturacion.Domain.Cajas.Caja.Crear(empresaId, "Caja Mostrador", null);
        db.Cajas.Add(caja);

        var cierreNocturno = new DateTimeOffset(2026, 5, 31, 1, 30, 0, TimeSpan.Zero);
        var delDia = Millet.Facturacion.Domain.Cajas.CajaSesion.Abrir(
            empresaId, caja.Id, Guid.NewGuid(), Guid.NewGuid(), 500m,
            new DateOnly(2026, 5, 30), new DateTimeOffset(2026, 5, 30, 14, 0, 0, TimeSpan.Zero), null);
        delDia.IniciarArqueo(new Dictionary<string, decimal> { ["01"] = 500m });
        delDia.Cerrar(500m, null, cierreExtemporaneo: false, cierreNocturno);
        db.CajaSesiones.Add(delDia);

        var diaAnterior = Millet.Facturacion.Domain.Cajas.CajaSesion.Abrir(
            empresaId, caja.Id, Guid.NewGuid(), Guid.NewGuid(), 500m,
            new DateOnly(2026, 5, 29), new DateTimeOffset(2026, 5, 29, 14, 0, 0, TimeSpan.Zero), null);
        diaAnterior.IniciarArqueo(new Dictionary<string, decimal> { ["01"] = 500m });
        diaAnterior.Cerrar(500m, null, cierreExtemporaneo: false, new DateTimeOffset(2026, 5, 30, 1, 0, 0, TimeSpan.Zero));
        db.CajaSesiones.Add(diaAnterior);
        await db.SaveChangesAsync();

        // Así llega «desde = hasta = 2026-05-30» al enlazar una fecha sin hora.
        var dia = new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero);
        var r = await new LiquidacionCajaHandler(db, new FakeClock(Ahora), new FakeUsuariosReadPort())
            .Handle(new LiquidacionCajaQuery(null, dia, dia), CancellationToken.None);

        var fila = r.Filas.Should().ContainSingle().Subject;
        fila.SesionId.Should().Be(delDia.Id);
        fila.DiaOperacion.Should().Be("2026-05-30");
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
