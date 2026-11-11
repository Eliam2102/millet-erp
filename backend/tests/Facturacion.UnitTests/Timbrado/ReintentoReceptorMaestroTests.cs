using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Application.Timbrado.ReintentarTimbrado;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// N-01 (FAC-EMISION/corregir): el reintento de una factura fallida toma los datos fiscales del cliente ya corregido en el
/// maestro, en lugar de exigir descartarla y emitirla de nuevo. Datos genéricos.
/// </summary>
public sealed class ReintentoReceptorMaestroTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 18, 0, 0, TimeSpan.Zero);

    private static FacturacionDbContext NewDb(Guid empresaId) =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(empresaId));

    private static (FacturaVenta Factura, Guid ClienteId) FacturaFallida(FacturacionDbContext db, Guid empresa, bool conPedido = true)
    {
        var pedido = PedidoFacturable.ImportarDesdeAw(empresa, "PED-N01", Guid.NewGuid(), Guid.NewGuid(), "Cliente prueba", 1,
            ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        db.PedidosFacturables.Add(pedido);
        // Receptor capturado con datos viejos: RFC, régimen y CP que luego se corrigieron en el maestro.
        var receptor = new DatosFiscalesReceptor("AAA010101AAA", "Cliente viejo", "601", "97000", "G03", "MEX", false);
        var fv = FacturaVenta.CrearBorrador(empresa, "FA-N01", 1, Guid.NewGuid(), null, null, receptor,
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, conPedido ? pedido.Id : null, null, null, false);
        fv.AgregarLinea(null, "01010101", "Vidrio", "H87", 1m, 100m, 0m, "02", 0.16m, null, null);
        fv.RecalcularTotales();
        fv.MarcarTimbradoEnProceso();
        fv.MarcarTimbradoFallido("CFDI40147", "El RFC del receptor no está en la lista de RFC inscritos");
        db.FacturasVenta.Add(fv);
        db.SaveChanges();
        return (fv, pedido.ClienteId);
    }

    private static ClienteFiscalLectura Maestro(Guid id, string? regimen = "612", string? cp = "97100") =>
        new(id, "CCC010101CCC", "Cliente corregido SA", regimen, cp, "G03", "01", "PUE", "MXN", false);

    private static ReintentarTimbradoHandler Handler(FacturacionDbContext db, ClienteFiscalLectura maestro, FakeFiscalApiClient pac) =>
        new(db, new FakeSender(new Millet.Administracion.Application.Series.ReservarFolioResponse("SIN-NUEVO-FOLIO", 2, "")),
            new FakePeriodoContablePort(true), pac, new FakeCfdiRepositorioPort(), new FakeIntegrationEventPublisher(), new FakeClock(Ahora),
            new ValidadorReceptorFiscal(new FakeClientesReadPort(maestro), new FakeCatalogosSatReadPort(), db));

    [Fact]
    public async Task El_reintento_con_el_cliente_corregido_en_el_maestro_timbra_con_sus_datos_vigentes_y_conserva_el_folio()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (factura, clienteId) = FacturaFallida(db, empresa);
        var pac = new FakeFiscalApiClient();

        var r = await Handler(db, Maestro(clienteId), pac).Handle(new ReintentarTimbradoCommand(factura.Id), CancellationToken.None);

        r.Folio.Should().Be("FA-N01");
        r.Estado.Should().Be("Timbrado");
        factura.ReceptorRfc.Should().Be("CCC010101CCC");
        factura.ReceptorNombre.Should().Be("Cliente corregido SA");
        factura.ReceptorRegimenFiscal.Should().Be("612");
        factura.ReceptorCodigoPostal.Should().Be("97100");
        factura.ReceptorUsoCfdi.Should().Be("G03"); // el uso es del documento
        pac.UltimaEmision!.Receptor.Rfc.Should().Be("CCC010101CCC");
    }

    [Fact]
    public async Task Sin_cambios_en_el_maestro_el_reintento_no_toca_el_receptor()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (factura, clienteId) = FacturaFallida(db, empresa);
        var igual = Maestro(clienteId) with { Rfc = "AAA010101AAA", RazonSocial = "Cliente viejo", RegimenFiscal = "601", CodigoPostalFiscal = "97000" };

        var r = await Handler(db, igual, new FakeFiscalApiClient()).Handle(new ReintentarTimbradoCommand(factura.Id), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        factura.ReceptorRfc.Should().Be("AAA010101AAA");
        factura.ReceptorNombre.Should().Be("Cliente viejo");
    }

    [Fact]
    public async Task Con_el_maestro_incompleto_el_reintento_se_rechaza_y_el_receptor_queda_igual()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (factura, clienteId) = FacturaFallida(db, empresa);

        await Assert.ThrowsAsync<ReceptorFiscalInvalidoException>(() =>
            Handler(db, Maestro(clienteId, regimen: null), new FakeFiscalApiClient())
                .Handle(new ReintentarTimbradoCommand(factura.Id), CancellationToken.None));

        factura.ReceptorRfc.Should().Be("AAA010101AAA");
        factura.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
    }

    [Fact]
    public async Task Una_factura_sin_cliente_resoluble_no_se_actualiza_y_sigue_la_regla_de_siempre()
    {
        var empresa = Guid.NewGuid();
        using var db = NewDb(empresa);
        var (factura, clienteId) = FacturaFallida(db, empresa, conPedido: false);

        var r = await Handler(db, Maestro(clienteId), new FakeFiscalApiClient()).Handle(new ReintentarTimbradoCommand(factura.Id), CancellationToken.None);

        r.Estado.Should().Be("Timbrado");
        factura.ReceptorRfc.Should().Be("AAA010101AAA"); // sin pedido no hay cliente al que consultar
    }

    [Fact]
    public void Un_comprobante_ya_timbrado_no_admite_actualizar_el_receptor()
    {
        var fv = FacturaVenta.CrearBorrador(Guid.NewGuid(), "FA-N01", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 11,
            (short)1, ComportamientoFiscal.MostradorInmediato, null, null, null, false);

        var act = () => fv.ActualizarReceptorParaReintento(new DatosFiscalesReceptor("CCC010101CCC", "X", "601", "97000", "G03", "MEX", false));

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "RECEPTOR_INMUTABLE");
    }
}
