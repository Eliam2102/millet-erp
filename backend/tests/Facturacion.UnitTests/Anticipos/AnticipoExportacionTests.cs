using Millet.Facturacion.Application.Anticipos;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Anticipos;

/// <summary>
/// 86e3zg0vb · anticipo de exportación (cliente extranjero XEXX en USD) y su NC de aplicación, contra los XML reales de Millet del
/// 6-nov: anticipo con IVA tasa 0 % y NumRegIdTrib; NC tipo E con IVA tasa 0 %, forma 30, uso G02 y la tasa del anticipo. El caso
/// nacional (IVA 16 %) no cambia. Datos ficticios.
/// </summary>
public sealed class AnticipoExportacionTests
{
    private static readonly DateTimeOffset AhoraUtc = new(2026, 11, 5, 16, 47, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesEmisor Emisor = new("AAA010101AAA", "Millet", "601", "97345");
    private static readonly DatosFiscalesReceptor Extranjero =
        new("XEXX010101000", "Cliente del exterior SA", "616", "97345", "S01", "DOM", true);
    private static readonly DatosFiscalesReceptor Nacional =
        new("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false);

    private static FacturaAnticipo Anticipo(DatosFiscalesReceptor receptor, decimal? tasa, string moneda = "USD", decimal monto = 485_282.09m) =>
        FacturaAnticipo.CrearBorrador(
            empresaId: Guid.NewGuid(), folio: "FANT-000001", folioNumero: 1, sucursalId: Guid.NewGuid(), cajaId: null,
            usuarioEmisorId: null, receptor: receptor, emisor: Emisor, formaPago: "03", moneda: moneda,
            tipoCambio: moneda == "MXN" ? null : 17.3752m, periodoAnio: 2026, periodoMes: 5,
            tipoAnticipo: moneda == "MXN" ? TipoAnticipo.ClientesMxp : TipoAnticipo.ClientesUsd, pedidoFacturableId: null,
            anticipoId: Guid.NewGuid(), montoBase: monto, tasaIvaTraslado: tasa, descripcion: "ANTICIPO DE CLIENTES USD");

    private static NotaCredito Aplicacion(DatosFiscalesReceptor receptorFactura, FacturaAnticipo anticipo, decimal importe,
        string moneda = "USD", decimal? tc = 18.7592m) =>
        NotaCredito.CrearAmortizacion(Guid.NewGuid(), "NCA-000001", 1, Guid.NewGuid(), null, null,
            NotaCredito.ReceptorAplicacion(receptorFactura), Emisor, NotaCredito.FormaPagoAplicacionAnticipo, moneda, tc, 2026, 11, 1,
            Guid.NewGuid(), Guid.NewGuid(), importe, anticipo.TasaIvaParaAplicacion());

    [Fact]
    public void Anticipo_a_cliente_extranjero_se_arma_con_iva_tasa_cero_y_su_numregidtrib()
    {
        var fa = Anticipo(Extranjero, 0m);
        fa.EstablecerNumRegIdTribReceptor("130044147");

        var e = CfdiEmisionBuilder.DesdeFacturaAnticipo(fa, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Ingreso);
        (e.Moneda, e.TipoCambio, e.MetodoPago, e.FormaPago, e.Exportacion).Should().Be(("USD", 17.3752m, "PUE", "03", "01"));
        e.Receptor.Should().Match<ReceptorCfdi>(r => r.Rfc == "XEXX010101000" && r.Pais == "DOM" && r.NumRegIdTrib == "130044147"
            && r.UsoCfdi == "S01" && r.RegimenFiscal == "616");
        var concepto = e.Conceptos.Single();
        (concepto.ClaveProdServ, concepto.ClaveUnidad, concepto.ObjetoImp, concepto.Importe).Should().Be(("84111506", "ACT", "02", 485_282.09m));
        concepto.Impuestos.Single().Should().Match<ImpuestoConceptoCfdi>(i =>
            i.Impuesto == "002" && i.TipoFactor == "Tasa" && i.TasaOCuota == 0m && i.Importe == 0m && i.Base == 485_282.09m);
        fa.Total.Should().Be(485_282.09m);
    }

    [Fact]
    public void Anticipo_a_publico_en_general_sigue_rechazado()
    {
        var act = () => Anticipo(Extranjero with { Rfc = "XAXX010101000", Pais = "MEX" }, 0.16m, "MXN");

        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_RECEPTOR_GENERICO");
    }

    [Fact]
    public void Anticipo_sin_tasa_sigue_exento_y_el_nacional_no_lleva_numregidtrib()
    {
        var fa = Anticipo(Nacional, null, "MXN", 1000m);
        fa.EstablecerNumRegIdTribReceptor("NO-APLICA");

        var e = CfdiEmisionBuilder.DesdeFacturaAnticipo(fa, AhoraUtc);

        e.Conceptos.Single().Impuestos.Single().TipoFactor.Should().Be("Exento");
        e.Receptor.NumRegIdTrib.Should().BeNull();
        fa.ReceptorNumRegIdTrib.Should().BeNull();
    }

    [Fact]
    public void Nc_de_aplicacion_parcial_del_anticipo_de_exportacion_va_con_tasa_cero_forma_30_uso_g02_y_tc_de_su_fecha()
    {
        var fa = Anticipo(Extranjero, 0m);
        var nc = Aplicacion(Extranjero, fa, 19_439.06m);
        nc.EstablecerNumRegIdTribReceptor("130044147");

        var e = CfdiEmisionBuilder.DesdeNotaCredito(nc, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Egreso);
        (e.MetodoPago, e.FormaPago, e.Exportacion, e.Moneda, e.TipoCambio).Should().Be(("PUE", "30", "01", "USD", 18.7592m));
        e.Receptor.Should().Match<ReceptorCfdi>(r => r.UsoCfdi == "G02" && r.NumRegIdTrib == "130044147" && r.Pais == "DOM");
        (nc.Subtotal, nc.ImpuestosTrasladados, nc.Total).Should().Be((19_439.06m, 0m, 19_439.06m));
        var concepto = e.Conceptos.Single();
        (concepto.ClaveProdServ, concepto.ClaveUnidad, concepto.ObjetoImp).Should().Be(("84111506", "ACT", "02"));
        concepto.Impuestos.Single().Should().Match<ImpuestoConceptoCfdi>(i => i.TipoFactor == "Tasa" && i.TasaOCuota == 0m && i.Importe == 0m);
    }

    [Fact]
    public void Nc_de_aplicacion_nacional_sigue_con_iva_16()
    {
        var fa = Anticipo(Nacional, 0.16m, "MXN", 1000m);
        var nc = Aplicacion(Nacional, fa, 1160m, "MXN", null);

        var e = CfdiEmisionBuilder.DesdeNotaCredito(nc, AhoraUtc);

        (nc.Subtotal, nc.ImpuestosTrasladados).Should().Be((1000m, 160m));
        var iva = e.Conceptos.Single().Impuestos.Single();
        (iva.TipoFactor, iva.TasaOCuota, iva.Importe).Should().Be(("Tasa", 0.16m, 160m));
        e.TipoCambio.Should().BeNull();
    }

    [Fact]
    public void Anticipo_previo_sin_tasa_guardada_la_reconstruye_de_sus_totales()
    {
        // Anticipo emitido antes de 86e3zg0vb: la tasa no se guardaba; con IVA se reconstruye (16 %) y sin IVA queda exento.
        var conIva = Anticipo(Nacional, 0.16m, "MXN", 1000m);
        typeof(FacturaAnticipo).GetProperty(nameof(FacturaAnticipo.TasaIvaTraslado))!.SetValue(conIva, null);
        conIva.TasaIvaParaAplicacion().Should().Be(0.16m);

        var sinIva = Anticipo(Nacional, null, "MXN", 1000m);
        sinIva.TasaIvaParaAplicacion().Should().BeNull();
    }

    [Theory]
    [InlineData("601", "G02")]
    [InlineData("616", "G02")]
    [InlineData("605", "G03")] // c_UsoCFDI: G02 no aplica al régimen 605; conserva el uso de la factura.
    public void La_nc_de_aplicacion_usa_g02_si_el_regimen_lo_admite(string regimen, string uso) =>
        NotaCredito.ReceptorAplicacion(Nacional with { RegimenFiscal = regimen }).UsoCfdi.Should().Be(uso);

    [Theory]
    [InlineData("S01", false, true)]
    [InlineData("G02", true, true)]
    [InlineData("G02", false, false)]
    public void El_rfc_generico_admite_g02_solo_en_nota_de_credito(string uso, bool esEgreso, bool valido)
    {
        var errores = ValidacionReceptorFiscal.Validar(Extranjero with { UsoCfdi = uso }, "97345", true, true, esEgreso: esEgreso);

        errores.Any(x => x.Campo == "usoCfdi").Should().Be(!valido);
    }

    [Fact]
    public void El_anticipo_se_cruza_por_cliente_y_con_rfc_generico_exige_el_cliente()
    {
        var clienteA = Guid.NewGuid();
        var anticipo = Domain.Anticipos.Anticipo.Crear(Guid.NewGuid(), clienteA, "XEXX010101000", TipoAnticipo.ClientesUsd, "USD", 1000m, Guid.NewGuid());

        var mismo = () => AnticiposDelCliente.Verificar([anticipo], clienteA, "XEXX010101000");
        var otro = () => AnticiposDelCliente.Verificar([anticipo], Guid.NewGuid(), "XEXX010101000");
        var sinCliente = () => AnticiposDelCliente.Verificar([anticipo], null, "XEXX010101000");
        var nacionalSinCliente = () => AnticiposDelCliente.Verificar(
            [Domain.Anticipos.Anticipo.Crear(Guid.NewGuid(), clienteA, "BBB010101BBB", TipoAnticipo.ClientesMxp, "MXN", 1000m, Guid.NewGuid())],
            null, "BBB010101BBB");

        mismo.Should().NotThrow();
        otro.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_CLIENTE_DISTINTO");
        sinCliente.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ANTICIPO_CLIENTE_REQUERIDO");
        nacionalSinCliente.Should().NotThrow();
    }
}
