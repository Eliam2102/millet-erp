using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.NotasCredito.EmitirNotaCreditoBonificacion;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.NotasCredito;

public sealed class BonificacionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    private static DatosFiscalesReceptor Receptor() => new(
        "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);

    private static async Task<FacturaVenta> SembrarFacturaAsync(
        FacturacionDbContext db, Guid empresaId, EstadoTimbrado estado = EstadoTimbrado.Timbrado)
    {
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-000001", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "03", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Producto", "H87", 1m, 1000m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        if (estado is EstadoTimbrado.Timbrado)
        {
            fv.MarcarTimbradoEnProceso();
            fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        }
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();
        return fv;
    }

    private static EmitirNotaCreditoBonificacionHandler Handler(
        FacturacionDbContext db, Guid empresaId, bool periodoAbierto = true) =>
        new(db, new FakeSender(new ReservarFolioResponse("NC-000001", 1, "")),
            new FakePeriodoContablePort(periodoAbierto), new FakeFiscalApiClient(),
            new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(), new FakeEmpresaContext(empresaId),
            new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

    // ---- Dominio ----

    [Fact]
    public void CrearBonificacion_es_egreso_motivo_bonificacion_con_totales()
    {
        var nc = NotaCredito.CrearBonificacion(
            Guid.NewGuid(), "NC-1", 1, Guid.NewGuid(), null, null, Receptor(),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "03", "MXN", null, 2026, 5,
            canalVentaId: 1, facturaRelacionadaId: Guid.NewGuid(), montoTotal: 116m, tasaIva: 0.16m, descripcion: null);

        nc.Tipo.Should().Be(TipoComprobante.Egreso);
        nc.Motivo.Should().Be(MotivoNotaCredito.Bonificacion);
        nc.AnticipoOrigenId.Should().BeNull();
        nc.Subtotal.Should().Be(100m);
        nc.ImpuestosTrasladados.Should().Be(16m);
        nc.Total.Should().Be(116m);
        nc.ImporteAfectaInventario.Should().Be(0m);
    }

    // ---- Handler ----

    [Fact]
    public async Task Emitir_bonificacion_timbra_NC_con_relacion_01()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = await SembrarFacturaAsync(db, empresaId);

        var resp = await Handler(db, empresaId).Handle(
            new EmitirNotaCreditoBonificacionCommand(factura.Id, 116m, 0.16m, "Descuento comercial"),
            CancellationToken.None);

        resp.Estado.Should().Be("Timbrado");
        resp.Uuid.Should().NotBeNullOrEmpty();

        var nc = await db.NotasCredito.Include(n => n.Relaciones).SingleAsync();
        nc.Motivo.Should().Be(MotivoNotaCredito.Bonificacion);
        nc.FacturaRelacionadaId.Should().Be(factura.Id);
        nc.Relaciones.Should().ContainSingle(r => r.TipoRelacion == "01" && r.UuidRelacionado == factura.Uuid);
    }

    [Fact]
    public async Task Emitir_sobre_factura_no_timbrada_es_rechazado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = await SembrarFacturaAsync(db, empresaId, EstadoTimbrado.Borrador);

        var act = () => Handler(db, empresaId).Handle(
            new EmitirNotaCreditoBonificacionCommand(factura.Id, 100m, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_TIMBRADA");
    }

    [Fact]
    public async Task Emitir_sobre_factura_inexistente_lanza_NotFound()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);

        var act = () => Handler(db, empresaId).Handle(
            new EmitirNotaCreditoBonificacionCommand(Guid.NewGuid(), 100m, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>().Where(e => e.Code == "FACTURA_NO_ENCONTRADA");
    }

    [Fact]
    public async Task Emitir_con_periodo_cerrado_lanza_PERIODO_CERRADO()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var factura = await SembrarFacturaAsync(db, empresaId);

        var act = () => Handler(db, empresaId, periodoAbierto: false).Handle(
            new EmitirNotaCreditoBonificacionCommand(factura.Id, 100m, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "PERIODO_CERRADO");
    }
}
