using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Cce;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Repp;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application.Exceptions;
using Dominio = Millet.Facturacion.Domain.CartaPorte;

namespace Millet.Facturacion.UnitTests.Timbrado;

/// <summary>
/// Tests del <see cref="CfdiEmisionBuilder"/> (F12-PR1): mapeo agregado →
/// contrato PAC-neutral. Cubre las responsabilidades transversales — fecha en
/// zona fiscal, tasas a 6 decimales, split Serie/Folio — y el shape por tipo.
/// </summary>
public sealed class CfdiEmisionBuilderTests
{
    private static readonly DateTimeOffset AhoraUtc = new(2026, 7, 9, 18, 30, 0, TimeSpan.Zero);
    private static readonly DatosFiscalesEmisor Emisor = new("AAA010101AAA", "Millet", "601", "76120");
    private static readonly DatosFiscalesReceptor Receptor =
        new("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false);

    private static FacturaVenta Factura(
        ComportamientoFiscal comportamiento = ComportamientoFiscal.MostradorInmediato) =>
        FacturaVenta.CrearBorrador(
            empresaId: Guid.NewGuid(), folio: "FA-000123", folioNumero: 123,
            sucursalId: Guid.NewGuid(), cajaId: null, usuarioEmisorId: null,
            receptor: Receptor, emisor: Emisor,
            metodoPago: "PUE", formaPago: "01", moneda: "MXN", tipoCambio: null,
            periodoAnio: 2026, periodoMes: 7, canalVentaId: 1,
            comportamientoFiscal: comportamiento,
            pedidoFacturableId: null, obraId: null, obraNombre: null, facturaAgrupada: false);

    [Fact]
    public void FechaFiscal_convierte_a_zona_de_Mexico()
    {
        var local = CfdiEmisionBuilder.FechaFiscal(AhoraUtc);

        // México abolió el horario de verano: America/Mexico_City es UTC-6 fijo.
        local.Offset.Should().Be(TimeSpan.FromHours(-6));
        local.UtcDateTime.Should().Be(AhoraUtc.UtcDateTime); // mismo instante
        local.Hour.Should().Be(12);
    }

    [Fact]
    public void DesdeFacturaVenta_mapea_emisor_receptor_y_split_serie_folio()
    {
        var f = Factura();
        f.AgregarLinea(null, "01010101", "Vidrio templado", "H87", 2m, 100m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();

        var e = CfdiEmisionBuilder.DesdeFacturaVenta(f, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Ingreso);
        e.Serie.Should().Be("FA");
        e.Folio.Should().Be("123");
        e.Emisor.Should().Be(new EmisorCfdi("AAA010101AAA", "Millet", "601", "76120"));
        e.Receptor.Rfc.Should().Be("BBB010101BBB");
        e.Receptor.CodigoPostal.Should().Be("64000");
        e.Exportacion.Should().Be("01");
        e.ReferenciaInterna.Should().Be(f.Id);
    }

    [Fact]
    public void DesdeFacturaVenta_construye_conceptos_con_impuestos_a_6_decimales()
    {
        var f = Factura();
        f.AgregarLinea(null, "01010101", "Con IVA y retenciones", "H87",
            cantidad: 1m, valorUnitario: 1000m, descuento: 100m, objetoImp: "02",
            tasaIvaTraslado: 0.16m, tasaRetencionIva: 0.106667m, tasaRetencionIsr: 0.0125m);
        f.RecalcularTotales();

        var concepto = CfdiEmisionBuilder.DesdeFacturaVenta(f, AhoraUtc).Conceptos.Single();

        concepto.Importe.Should().Be(1000m);
        concepto.Descuento.Should().Be(100m);
        concepto.Impuestos.Should().HaveCount(3);

        var traslado = concepto.Impuestos.Single(i => !i.EsRetencion);
        traslado.Impuesto.Should().Be("002");
        traslado.TipoFactor.Should().Be("Tasa");
        traslado.Base.Should().Be(900m);
        traslado.TasaOCuota.Should().Be(0.16m);
        traslado.Importe.Should().Be(144m);

        var retIva = concepto.Impuestos.Single(i => i.EsRetencion && i.Impuesto == "002");
        retIva.TasaOCuota.Should().Be(0.106667m);
        var retIsr = concepto.Impuestos.Single(i => i.EsRetencion && i.Impuesto == "001");
        retIsr.TasaOCuota.Should().Be(0.0125m);
    }

    [Fact]
    public void DesdeFacturaVenta_linea_no_objeto_de_impuesto_no_lleva_impuestos()
    {
        var f = Factura();
        f.AgregarLinea(null, "01010101", "No objeto", "H87", 1m, 100m, 0m, "01", null, null, null);
        f.RecalcularTotales();

        var concepto = CfdiEmisionBuilder.DesdeFacturaVenta(f, AhoraUtc).Conceptos.Single();

        concepto.ObjetoImp.Should().Be("01");
        concepto.Impuestos.Should().BeEmpty();
    }

    [Fact]
    public void DesdeFacturaVenta_incluye_pedimento_y_relaciones()
    {
        var f = Factura();
        f.AgregarLinea(null, "01010101", "Importado", "H87", 1m, 500m, 0m, "02",
            0.16m, null, null, requierePedimento: true);
        f.RecalcularTotales();
        f.MarcarPendientePedimento();
        f.AplicarPedimento("24 47 3807 4002345", new DateOnly(2026, 6, 1), "LOTE-9");
        f.AgregarRelacion("11111111-1111-1111-1111-111111111111", "07");

        var e = CfdiEmisionBuilder.DesdeFacturaVenta(f, AhoraUtc);

        var pedimento = e.Conceptos.Single().Pedimento;
        pedimento.Should().NotBeNull();
        pedimento!.Numero.Should().Be("24 47 3807 4002345");
        pedimento.FechaDocAduanero.Should().Be(new DateOnly(2026, 6, 1));

        e.Relaciones.Should().ContainSingle(r =>
            r.TipoRelacion == "07" && r.Uuid == "11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void DesdeFacturaVenta_con_cce_marca_exportacion_02_y_mapea_mercancias_y_domicilio()
    {
        var f = Factura(ComportamientoFiscal.ExportacionConCce);
        f.AgregarLinea(null, "01010101", "Export", "H87", 1m, 1000m, 0m, "02", 0m, null, null);
        f.RecalcularTotales();
        var cce = ComplementoCce.Crear(f.Id, "2", "FOB", 18.5m, "US123456789", "USA",
            claveDePedimento: "A1", certificadoOrigen: true,
            receptorDomicilioCalle: "123 Main St", receptorDomicilioEstado: "TX",
            receptorDomicilioCodigoPostal: "75001");
        cce.AgregarLinea("70109999", "06", 1m, 55m, 55m, aplicaIva0: true);
        f.AdjuntarCce(cce);

        var e = CfdiEmisionBuilder.DesdeFacturaVenta(f, AhoraUtc);

        e.Exportacion.Should().Be("02");
        e.Receptor.NumRegIdTrib.Should().Be("US123456789");
        e.ComplementoCce.Should().NotBeNull();
        e.ComplementoCce!.Incoterm.Should().Be("FOB");
        e.ComplementoCce.TipoCambioUsd.Should().Be(18.5m);
        e.ComplementoCce.ClaveDePedimento.Should().Be("A1");
        e.ComplementoCce.CertificadoOrigen.Should().BeTrue();
        e.ComplementoCce.Domicilio.Should().Be(new DomicilioCceCfdi("123 Main St", "TX", "USA", "75001"));
        var mercancia = e.ComplementoCce.Mercancias.Single();
        mercancia.FraccionArancelaria.Should().Be("70109999");
        // P10-H2: la mercancía CCE se liga a su concepto por NoIdentificacion ==
        // ItemSku (contrato FiscalAPI). El SKU es único por línea (clave + posición),
        // no el valor trivial "1" que colisionaba en el sandbox.
        mercancia.NoIdentificacion.Should().Be("01010101-1");
        var concepto = e.Conceptos.Single();
        concepto.NoIdentificacion.Should().Be(mercancia.NoIdentificacion);
    }

    [Fact]
    public void DesdeFacturaVenta_con_cce_sin_estado_del_domicilio_lanza_pre_vuelo()
    {
        var f = Factura(ComportamientoFiscal.ExportacionConCce);
        f.AgregarLinea(null, "01010101", "Export", "H87", 1m, 1000m, 0m, "02", 0m, null, null);
        f.RecalcularTotales();
        var cce = ComplementoCce.Crear(f.Id, "2", "FOB", 18.5m, "US123456789", "USA",
            receptorDomicilioCodigoPostal: "75001"); // sin estado
        cce.AgregarLinea("70109999", "06", 1m, 55m, 55m, aplicaIva0: true);
        f.AdjuntarCce(cce);

        var act = () => CfdiEmisionBuilder.DesdeFacturaVenta(f, AhoraUtc);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CCE_DOMICILIO_RECEPTOR_INCOMPLETO");
    }

    [Fact]
    public void DesdeNotaCredito_es_egreso_con_concepto_unico_y_tasa_reconstruida()
    {
        var nc = NotaCredito.CrearBonificacion(
            empresaId: Guid.NewGuid(), folio: "NC-000009", folioNumero: 9,
            sucursalId: Guid.NewGuid(), cajaId: null, usuarioEmisorId: null,
            receptor: Receptor, emisor: Emisor,
            formaPago: "01", moneda: "MXN", tipoCambio: null,
            periodoAnio: 2026, periodoMes: 7, canalVentaId: 1,
            facturaRelacionadaId: Guid.NewGuid(), montoTotal: 116m, tasaIva: 0.16m,
            descripcion: "Bonificación de prueba");

        var e = CfdiEmisionBuilder.DesdeNotaCredito(nc, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Egreso);
        e.Serie.Should().Be("NC");
        e.Folio.Should().Be("9");
        var concepto = e.Conceptos.Single();
        concepto.ClaveProdServ.Should().Be("84111506");
        concepto.Importe.Should().Be(nc.Subtotal);
        var iva = concepto.Impuestos.Single();
        iva.TipoFactor.Should().Be("Tasa");
        iva.TasaOCuota.Should().Be(0.16m);
        iva.Importe.Should().Be(nc.ImpuestosTrasladados);
    }

    [Fact]
    public void DesdeFacturaAnticipo_es_ingreso_con_concepto_84111506()
    {
        var fa = FacturaAnticipo.CrearBorrador(
            empresaId: Guid.NewGuid(), folio: "FANT-000002", folioNumero: 2,
            sucursalId: Guid.NewGuid(), cajaId: null, usuarioEmisorId: null,
            receptor: Receptor, emisor: Emisor,
            formaPago: "03", moneda: "MXN", tipoCambio: null,
            periodoAnio: 2026, periodoMes: 7,
            tipoAnticipo: TipoAnticipo.ClientesMxp, pedidoFacturableId: null,
            anticipoId: Guid.NewGuid(), montoBase: 1000m, tasaIvaTraslado: 0.16m);

        var e = CfdiEmisionBuilder.DesdeFacturaAnticipo(fa, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Ingreso);
        e.Serie.Should().Be("FANT");
        var concepto = e.Conceptos.Single();
        concepto.ClaveProdServ.Should().Be("84111506");
        concepto.Impuestos.Single().TasaOCuota.Should().Be(0.16m);
    }

    [Fact]
    public void DesdeReciboPago_es_tipo_pago_con_concepto_fijo_y_documentos()
    {
        var repp = ReciboPago.CrearBorrador(
            empresaId: Guid.NewGuid(), folio: "FA-000200", folioNumero: 200,
            sucursalId: Guid.NewGuid(), cajaId: null, usuarioEmisorId: null,
            receptor: Receptor, emisor: Emisor,
            periodoAnio: 2026, periodoMes: 7,
            fechaPago: AhoraUtc.AddDays(-1), monedaPago: "USD");
        repp.AgregarFacturaPagada(
            facturaVentaId: Guid.NewGuid(), facturaUuid: "22222222-2222-2222-2222-222222222222",
            numParcialidad: 2, monedaFactura: "USD", importePagado: 580m, saldoAnterior: 1160m,
            formaPagoReal: "03", tcFactura: 17.0m, tcPago: 18.0m,
            cuentaOrdenante: "012345", cuentaBeneficiaria: "678901", referenciaPago: "REF-1",
            objetoImpDR: "02", facturaTotal: 1160m,
            impuestosFactura: [new ImpuestoFacturaPagada("002", "Tasa", 0.16m, EsRetencion: false, BaseGravable: 1000m)]);
        repp.EstablecerImporteTotalPago(580m);

        var e = CfdiEmisionBuilder.DesdeReciboPago(repp, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Pago);
        e.Moneda.Should().Be("XXX");
        // SAT: el CFDI tipo P siempre lleva UsoCFDI CP01, no el de la factura.
        e.Receptor.UsoCfdi.Should().Be("CP01");
        var concepto = e.Conceptos.Single();
        concepto.ClaveProdServ.Should().Be("84111506");
        concepto.ClaveUnidad.Should().Be("ACT");
        concepto.ValorUnitario.Should().Be(0m);
        concepto.ObjetoImp.Should().Be("01");

        e.ComplementoPago.Should().NotBeNull();
        e.ComplementoPago!.Moneda.Should().Be("USD");
        e.ComplementoPago.TipoCambio.Should().Be(18.0m);
        e.ComplementoPago.Monto.Should().Be(580m);
        var doc = e.ComplementoPago.Documentos.Single();
        doc.FacturaUuid.Should().Be("22222222-2222-2222-2222-222222222222");
        doc.NumParcialidad.Should().Be(2);
        doc.SaldoAnterior.Should().Be(1160m);
        doc.ImportePagado.Should().Be(580m);
        doc.SaldoInsoluto.Should().Be(580m);

        // ImpuestosDR prorrateado (factor 580/1160 = 0.5): base 500, IVA 80.
        doc.ObjetoImpDR.Should().Be("02");
        doc.Equivalencia.Should().Be(1m); // pago USD, factura USD
        doc.Subtotal.Should().Be(500m);
        var impuesto = doc.Impuestos!.Single();
        impuesto.Impuesto.Should().Be("002");
        impuesto.TipoFactor.Should().Be("Tasa");
        impuesto.TasaOCuota.Should().Be(0.16m);
        impuesto.EsRetencion.Should().BeFalse();
        impuesto.BaseDR.Should().Be(500m);
    }

    private static Dominio.CartaPorte CartaPorteBorrador(
        string? origenCp = "76120", string? origenEstado = "QUE",
        string? destinoCp = "06600", string? destinoEstado = "CMX",
        Guid? previaId = null)
    {
        return Dominio.CartaPorte.CrearBorrador(
            Guid.NewGuid(), TipoComprobante.Traslado, "FA-000300", 300,
            Guid.NewGuid(), null, null, Receptor, Emisor, "MXN", 2026, 7,
            origen: "Querétaro", destino: "CDMX", distanciaKm: 220m,
            vehiculoId: Guid.NewGuid(), operadorId: Guid.NewGuid(),
            cartaPortePreviaId: previaId, pedidoFacturableId: null,
            fechaSalida: AhoraUtc, fechaLlegadaEstimada: AhoraUtc.AddHours(4),
            origenCodigoPostal: origenCp, origenEstado: origenEstado,
            destinoCodigoPostal: destinoCp, destinoEstado: destinoEstado);
    }

    private static Dominio.Vehiculo VehiculoCatalogo(decimal? pesoBruto = 17.5m) =>
        Dominio.Vehiculo.Crear(Guid.NewGuid(), "ABC1234", "C2R2", 2022, "TPAF01", "PERM-1",
            pesoBrutoVehicular: pesoBruto);

    [Fact]
    public void DesdeCartaPorte_traslado_espeja_mercancias_con_valor_cero()
    {
        var previaId = Guid.CreateVersion7();
        var cp = CartaPorteBorrador(previaId: previaId);
        cp.AgregarMercancia("Vidrio laminado", "43211508", "H87", 10m, 850m, materialPeligroso: false);

        var vehiculo = VehiculoCatalogo();
        var operador = Dominio.Operador.Crear(cp.EmpresaId, "CCC010101CC1", "Juan Chofer", "LIC-99");

        var e = CfdiEmisionBuilder.DesdeCartaPorte(cp, vehiculo, operador, AhoraUtc);

        e.Tipo.Should().Be(TipoCfdi.Traslado);
        var concepto = e.Conceptos.Single();
        concepto.ClaveProdServ.Should().Be("43211508");
        concepto.ValorUnitario.Should().Be(0m);
        concepto.ObjetoImp.Should().Be("01");
        concepto.Impuestos.Should().BeEmpty();

        e.ComplementoCartaPorte.Should().NotBeNull();
        e.ComplementoCartaPorte!.Vehiculo.Placa.Should().Be("ABC1234");
        e.ComplementoCartaPorte.Vehiculo.PesoBrutoVehicular.Should().Be(17.5m);
        e.ComplementoCartaPorte.Operador.NumLicencia.Should().Be("LIC-99");
        e.ComplementoCartaPorte.IdCcpRelacionado.Should().Be(previaId.ToString("D"));
        e.ComplementoCartaPorte.Mercancias.Single().PesoEnKg.Should().Be(850m);
    }

    [Fact]
    public void DesdeCartaPorte_mapea_ubicaciones_con_domicilio_y_rfcs()
    {
        var cp = CartaPorteBorrador();
        cp.AgregarMercancia("Vidrio laminado", "43211508", "H87", 10m, 850m, materialPeligroso: false);

        var e = CfdiEmisionBuilder.DesdeCartaPorte(
            cp, VehiculoCatalogo(), Dominio.Operador.Crear(cp.EmpresaId, "CCC010101CC1", "Juan Chofer", "LIC-99"), AhoraUtc);

        var c = e.ComplementoCartaPorte!;
        c.Origen.Should().Be(new UbicacionCartaPorteCfdi("Querétaro", "76120", "QUE", "MEX"));
        c.Destino.Should().Be(new UbicacionCartaPorteCfdi("CDMX", "06600", "CMX", "MEX"));
        c.RfcRemitente.Should().Be(Emisor.Rfc);
        c.NombreRemitente.Should().Be(Emisor.Nombre);
        c.RfcDestinatario.Should().Be(Receptor.Rfc);
        c.NombreDestinatario.Should().Be(Receptor.Nombre);
    }

    [Theory]
    [InlineData(true, false)]  // falta CP destino
    [InlineData(false, true)]  // falta peso bruto vehicular
    public void DesdeCartaPorte_sin_datos_sat_lanza_pre_vuelo(bool sinCpDestino, bool sinPesoBruto)
    {
        var cp = CartaPorteBorrador(destinoCp: sinCpDestino ? null : "06600");
        cp.AgregarMercancia("Vidrio laminado", "43211508", "H87", 10m, 850m, materialPeligroso: false);
        var vehiculo = VehiculoCatalogo(pesoBruto: sinPesoBruto ? null : 17.5m);
        var operador = Dominio.Operador.Crear(cp.EmpresaId, "CCC010101CC1", "Juan Chofer", "LIC-99");

        var act = () => CfdiEmisionBuilder.DesdeCartaPorte(cp, vehiculo, operador, AhoraUtc);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "CARTA_PORTE_DATOS_SAT_INCOMPLETOS");
    }

    [Fact]
    public void EmisorDe_sin_lugar_expedicion_lanza_EMISOR_SIN_LUGAR_EXPEDICION()
    {
        var act = () => new DatosFiscalesEmisor("AAA010101AAA", "Millet", "601", "").Validar();

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "EMISOR_SIN_LUGAR_EXPEDICION");
    }
}
