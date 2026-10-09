using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Facturas;

public sealed class DetalleCobroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesReceptor Receptor = new("AAA010101AAA", "Cliente ficticio FAC-05", "601", "97000", "G03", "MEX", false);
    private static readonly DatosFiscalesEmisor Emisor = new("BBB010101BBB", "Emisor ficticio", "601", "97000");

    private static FacturaVenta Factura(Guid empresa, string metodo)
    {
        var f = FacturaVenta.CrearBorrador(empresa, "FIX-FAC-05", 1, Guid.NewGuid(), null, null,
            Receptor, Emisor, metodo, metodo == "PUE" ? "01" : "99", "MXN", null,
            2026, 10, 1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba ficticia", "H87", 1, 100, 0, "02", 0.16m, null, null);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        return f;
    }

    private static FacturacionDbContext Db(Guid empresa) => new(
        new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeEmpresaContext(empresa));

    private static Task<ComprobanteDetalleResponse> Detalle(FacturacionDbContext db, Guid id) =>
        new ComprobanteDetalleHandler(db, new FakeAlcanceCajaEvaluator()).Handle(new(id), CancellationToken.None);

    [Fact]
    public async Task Pue_antes_despues_cancelado_y_recobrado_conserva_nc_y_expone_desglose()
    {
        var empresa = Guid.NewGuid();
        using var db = Db(empresa);
        var f = Factura(empresa, "PUE");
        var nc = NotaCredito.CrearAmortizacion(empresa, "FIX-NC", 2, f.SucursalId, null, null,
            Receptor, Emisor, "01", "MXN", null, 2026, 10, 1, Guid.NewGuid(), f.Id, 16m, null);
        nc.MarcarTimbradoEnProceso();
        nc.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        var caja = Caja.Crear(empresa, "Caja ficticia FAC-05", null);
        var usuario = Guid.NewGuid();
        var sesion = CajaSesion.Abrir(empresa, caja.Id, f.SucursalId, usuario, 0, new(2026, 10, 9), Ahora, null);
        db.AddRange(f, nc, caja, sesion);
        await db.SaveChangesAsync();

        var antes = await Detalle(db, f.Id);
        antes.MetodoPago.Should().Be("PUE");
        antes.CobroMostrador.Should().BeNull();
        antes.TotalPorCobrar.Should().Be(100m);

        var cobro = CobroMostrador.Registrar(empresa, sesion.Id, f.SucursalId, 1, f.Id,
            OrigenCobroMostrador.Mostrador, Ahora, usuario,
            [("01", 60m, null, null, null), ("04", 40m, "AUT-FICTICIA", null, null)]);
        db.Add(cobro);
        await db.SaveChangesAsync();
        var despues = await Detalle(db, f.Id);
        despues.TotalPorCobrar.Should().Be(0m);
        despues.TotalAcreditado.Should().Be(16m);
        despues.PagadoPorRep.Should().Be(0m);
        var detalle = despues.CobroMostrador!;
        detalle.Id.Should().Be(cobro.Id);
        detalle.UsuarioCobradorId.Should().Be(usuario);
        detalle.FechaCobro.Should().Be(Ahora);
        detalle.Total.Should().Be(100m);
        detalle.Sesion.Id.Should().Be(sesion.Id);
        detalle.Sesion.CajaNombre.Should().Be(caja.Nombre);
        detalle.FormasPago.Should().HaveCount(2);
        detalle.FormasPago.Should().Contain(p => p.FormaPago == "04" && p.Importe == 40m && p.Referencia == "AUT-FICTICIA");

        cobro.Cancelar();
        await db.SaveChangesAsync();
        var cancelado = await Detalle(db, f.Id);
        cancelado.CobroMostrador.Should().BeNull();
        cancelado.TotalPorCobrar.Should().Be(100m);

        var nuevo = CobroMostrador.Registrar(empresa, sesion.Id, f.SucursalId, 1, f.Id,
            OrigenCobroMostrador.Mostrador, Ahora.AddMinutes(1), usuario, [("01", 100m, null, null, null)]);
        db.Add(nuevo);
        await db.SaveChangesAsync();
        (await Detalle(db, f.Id)).CobroMostrador!.Id.Should().Be(nuevo.Id);
    }

    [Theory]
    [InlineData(EstadoTimbrado.Borrador, 0)]
    [InlineData(EstadoTimbrado.TimbradoEnProceso, 0)]
    [InlineData(EstadoTimbrado.TimbradoFallido, 0)]
    [InlineData(EstadoTimbrado.Descartada, 0)]
    [InlineData(EstadoTimbrado.CancelacionPendiente, 40)]
    [InlineData(EstadoTimbrado.Cancelado, 0)]
    [InlineData(EstadoTimbrado.Timbrado, 40)]
    public async Task Ppd_solo_descuenta_rep_timbrados(EstadoTimbrado estado, decimal esperado)
    {
        var empresa = Guid.NewGuid();
        using var db = Db(empresa);
        var f = Factura(empresa, "PPD");
        var rep = ReciboPago.CrearBorrador(empresa, "FIX-REP", 2, f.SucursalId, null, null,
            Receptor, Emisor, 2026, 10, Ahora, "MXN", 1);
        rep.AgregarFacturaPagada(f.Id, f.Uuid!, 1, "MXN", 40m, 116m, "03", null, null,
            null, null, "FIX-REP", "01", f.Total, []);
        rep.EstablecerImporteTotalPago(40m);
        if (estado != EstadoTimbrado.Borrador) rep.MarcarTimbradoEnProceso();
        if (estado is EstadoTimbrado.TimbradoFallido or EstadoTimbrado.Descartada)
            rep.MarcarTimbradoFallido("FIX", "Rechazo ficticio");
        if (estado == EstadoTimbrado.Descartada) rep.Descartar();
        if (estado is EstadoTimbrado.Timbrado or EstadoTimbrado.CancelacionPendiente or EstadoTimbrado.Cancelado)
            rep.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, Ahora, null, null);
        if (estado is EstadoTimbrado.CancelacionPendiente or EstadoTimbrado.Cancelado) rep.MarcarCancelacionPendiente();
        if (estado == EstadoTimbrado.Cancelado) rep.MarcarCancelado();
        db.AddRange(f, rep);
        await db.SaveChangesAsync();

        var detalle = await Detalle(db, f.Id);
        detalle.MetodoPago.Should().Be("PPD");
        detalle.CobroMostrador.Should().BeNull();
        detalle.PagadoPorRep.Should().Be(esperado);
        detalle.TotalPorCobrar.Should().Be(116m - esperado);
    }
}
