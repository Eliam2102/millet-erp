using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

public sealed class FacturaVentaAggregateTests
{
    private static FacturaVenta Borrador() => FacturaVenta.CrearBorrador(
        empresaId: Guid.NewGuid(),
        folio: "FA-000001",
        folioNumero: 1,
        sucursalId: Guid.NewGuid(),
        cajaId: null,
        usuarioEmisorId: null,
        receptor: new DatosFiscalesReceptor("XAXX010101000", "Público en general", "616", "97000", "S01", "MEX", true),
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

    private static void AgregarLineaSimple(FacturaVenta f) =>
        f.AgregarLinea(null, "01010101", "Producto de prueba", "H87",
            cantidad: 2m, valorUnitario: 100m, descuento: 0m, objetoImp: "02",
            tasaIvaTraslado: 0.16m, tasaRetencionIva: null, tasaRetencionIsr: null);

    [Fact]
    public void CrearBorrador_arranca_en_Borrador_tipo_Ingreso()
    {
        var f = Borrador();

        f.Estado.Should().Be(EstadoTimbrado.Borrador);
        f.Tipo.Should().Be(TipoComprobante.Ingreso);
        f.ReceptorEsGenerico.Should().BeTrue();
        f.Uuid.Should().BeNull();
    }

    [Fact]
    public void AgregarLinea_y_RecalcularTotales_calcula_subtotal_impuestos_total()
    {
        var f = Borrador();
        AgregarLineaSimple(f);

        f.RecalcularTotales();

        f.Subtotal.Should().Be(200m);
        f.ImpuestosTrasladados.Should().Be(32m);
        f.Retenciones.Should().Be(0m);
        f.Total.Should().Be(232m);
        f.Lineas.Should().HaveCount(1);
        f.Lineas.Single().Importe.Should().Be(200m);
    }

    [Fact]
    public void RecalcularTotales_sin_lineas_lanza()
    {
        var f = Borrador();

        var act = () => f.RecalcularTotales();

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_SIN_LINEAS");
    }

    [Fact]
    public void AgregarLinea_sin_clave_sat_lanza()
    {
        var f = Borrador();

        var act = () => f.AgregarLinea(null, "", "Sin clave", "H87", 1m, 10m, 0m, "02", null, null, null);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "LINEA_SIN_CLAVE_SAT");
    }

    [Fact]
    public void MarcarTimbrado_requiere_pasar_por_TimbradoEnProceso()
    {
        var f = Borrador();
        AgregarLineaSimple(f);
        f.RecalcularTotales();

        var act = () => f.MarcarTimbrado("uuid", null, null, null, DateTimeOffset.UtcNow, null, null);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "COMPROBANTE_NO_EN_PROCESO");
    }

    [Fact]
    public void Flujo_timbrado_feliz_deja_Timbrado_con_uuid_y_archivo()
    {
        var f = Borrador();
        AgregarLineaSimple(f);
        f.RecalcularTotales();
        var archivoId = Guid.NewGuid();

        f.MarcarTimbradoEnProceso();
        f.Estado.Should().Be(EstadoTimbrado.TimbradoEnProceso);

        f.MarcarTimbrado("UUID-1", "selloCfdi", "selloSat", "20001000000300022815",
            new DateTimeOffset(2026, 5, 30, 0, 0, 0, TimeSpan.Zero), "SAT970701NN3", archivoId);

        f.Estado.Should().Be(EstadoTimbrado.Timbrado);
        f.Uuid.Should().Be("UUID-1");
        f.CfdiArchivoId.Should().Be(archivoId);
    }

    [Fact]
    public void MarcarTimbradoFallido_desde_EnProceso_guarda_error()
    {
        var f = Borrador();
        AgregarLineaSimple(f);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();

        f.MarcarTimbradoFallido("CFDI40110", "RFC del receptor inválido");

        f.Estado.Should().Be(EstadoTimbrado.TimbradoFallido);
        f.TimbradoErrorCodigo.Should().Be("CFDI40110");
    }

    [Fact]
    public void AgregarLinea_tras_pasar_a_proceso_lanza_inmutable()
    {
        var f = Borrador();
        AgregarLineaSimple(f);
        f.RecalcularTotales();
        f.MarcarTimbradoEnProceso();

        var act = () => AgregarLineaSimple(f);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_INMUTABLE");
    }
}
