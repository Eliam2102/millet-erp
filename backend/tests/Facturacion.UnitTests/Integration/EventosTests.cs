using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.Anticipos.EmitirFacturaAnticipo;
using Millet.Facturacion.Application.Cancelaciones.SolicitarCancelacion;
using Millet.Facturacion.Application.Facturas.EmitirFacturaVenta;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Integration;

/// <summary>F10-PR1 — los handlers publican los eventos de integración al timbrar/cancelar.</summary>
public sealed class EventosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new FakeEmpresaContext(empresaId));

    [Fact]
    public async Task Emitir_factura_publica_FacturaVentaTimbrada()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var eventos = new FakeIntegrationEventPublisher();
        var contabilidad = new FakeContabilidadAsientoPort();
        var handler = new EmitirFacturaVentaHandler(
            db, new FakeSender(new ReservarFolioResponse("F-1", 1, "")), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            eventos, contabilidad, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

        await handler.Handle(new EmitirFacturaVentaCommand(
            Guid.NewGuid(), "XAXX010101000", "Público", "616", "97000", "S01", "MEX",
            "BBB010101BBB", "601", "PUE", "01", "MXN", null,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, false,
            [new EmitirFacturaVentaLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null)]),
            CancellationToken.None);

        eventos.Publicados.OfType<FacturaVentaTimbradaIntegrationEvent>().Should().ContainSingle()
            .Which.EventType.Should().Be("facturacion.factura-venta.timbrada.v1");
        contabilidad.Veces.Should().Be(1);
    }

    [Fact]
    public async Task Emitir_anticipo_publica_FacturaAnticipoTimbrada()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var eventos = new FakeIntegrationEventPublisher();
        var handler = new EmitirFacturaAnticipoHandler(
            db, new FakeSender(new ReservarFolioResponse("FANT-1", 1, "")), new FakePeriodoContablePort(),
            new FakeCatalogosSatReadPort(), new FakeFiscalApiClient(), new FakeCfdiRepositorioPort(),
            new FakeEmpresaFiscalReadPort(new EmpresaFiscalLectura(empresaId, "MIL010101AAA", "Millet", "601", 0.16m, "76120")),
            eventos, new FakeEmpresaContext(empresaId), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora));

        await handler.Handle(new EmitirFacturaAnticipoCommand(
            Guid.NewGuid(), Guid.NewGuid(), "AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX",
            "BBB010101BBB", "601", "PUE", "03", "MXN", null, TipoAnticipo.ClientesMxp, 1000m, 0.16m, null, null, "AW-1", null, null),
            CancellationToken.None);

        eventos.Publicados.OfType<FacturaAnticipoTimbradaIntegrationEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelar_factura_publica_ComprobanteCancelado()
    {
        var empresaId = Guid.NewGuid();
        using var db = NewDb(empresaId);
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresaId, "F-1", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 5,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        fv.AgregarLinea(null, "01010101", "P", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbrado("11111111-1111-1111-1111-111111111111", null, null, null, Ahora, null, null);
        db.FacturasVenta.Add(fv);
        await db.SaveChangesAsync();

        var eventos = new FakeIntegrationEventPublisher();
        var handler = new SolicitarCancelacionHandler(db, new FakeFiscalApiClient(), eventos, new FakeEmpresaContext(empresaId), new FakeClock(Ahora));
        await handler.Handle(new SolicitarCancelacionCommand(fv.Id, "02", null), CancellationToken.None);

        eventos.Publicados.OfType<ComprobanteCanceladoIntegrationEvent>().Should().ContainSingle()
            .Which.Uuid.Should().Be("11111111-1111-1111-1111-111111111111");
    }
}
