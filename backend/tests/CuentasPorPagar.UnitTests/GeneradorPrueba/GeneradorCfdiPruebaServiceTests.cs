using FluentAssertions;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.GeneradorPrueba;
using Millet.CuentasPorPagar.Infrastructure.Parsing;
using Xunit;

namespace Millet.CuentasPorPagar.UnitTests.GeneradorPrueba;

public sealed class GeneradorCfdiPruebaServiceTests
{
    private readonly GeneradorCfdiPruebaService _generador = new();
    private readonly XmlCfdiParser _parser = new();

    [Fact]
    public void GenerarXml_cfdi_basico_una_linea_iva_16_produce_xml_valido_y_parseable()
    {
        var uuid = Guid.Parse("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        var fecha = new DateTimeOffset(2026, 5, 22, 10, 0, 0, TimeSpan.FromHours(-6));

        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor SA"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet SA", CodigoPostal: "97300"),
            Lineas:
            [
                new LineaCfdiPrueba(
                    Posicion: 1,
                    Cantidad: 2m,
                    PrecioUnitario: 500m,
                    ArticuloClave: "ART-001",
                    ArticuloNombre: "Servicio de prueba",
                    ClaveProdServ: "01010101",
                    ClaveUnidad: "H87",
                    UnidadMedida: "PZA",
                    TasaIva: 0.16m)
            ],
            Folio: "F123",
            Serie: "PRUEBA",
            Moneda: "MXN",
            MetodoPago: "PPD",
            Uuid: uuid,
            Fecha: fecha);

        var xml = _generador.GenerarXml(datos);

        xml.Should().NotBeNullOrWhiteSpace();
        xml.Should().Contain("xmlns:cfdi=\"http://www.sat.gob.mx/cfd/4\"");
        xml.Should().Contain("xmlns:tfd=\"http://www.sat.gob.mx/TimbreFiscalDigital\"");
        xml.Should().Contain("Serie=\"PRUEBA\"");
        xml.Should().Contain("Folio=\"F123\"");
        xml.Should().Contain("<!-- CFDI DE PRUEBA");

        var parseado = _parser.Parsear(xml);

        parseado.UuidCfdi.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        parseado.RfcEmisor.Should().Be("PROV010101AAA");
        parseado.RazonSocialEmisor.Should().Be("Proveedor SA");
        parseado.RfcReceptor.Should().Be("MIL010101AAA");
        parseado.RazonSocialReceptor.Should().Be("Millet SA");
        parseado.Tipo.Should().Be(TipoCfdi.Ingreso);
        parseado.Subtotal.Should().Be(1000.00m);
        parseado.Descuentos.Should().Be(0m);
        parseado.ImpuestosTrasladados.Should().Be(160.00m);
        parseado.Retenciones.Should().Be(0m);
        parseado.Total.Should().Be(1160.00m);
        parseado.Moneda.Should().Be("MXN");
        parseado.TipoCambio.Should().BeNull();
        parseado.Folio.Should().Be("F123");
        parseado.Serie.Should().Be("PRUEBA");
        parseado.MetodoPago.Should().Be("PPD");

        parseado.Lineas.Should().HaveCount(1);
        var l1 = parseado.Lineas[0];
        l1.Posicion.Should().Be(1);
        l1.NoIdentificacion.Should().Be("ART-001");
        l1.ClaveProdServ.Should().Be("01010101");
        l1.ClaveUnidad.Should().Be("H87");
        l1.Unidad.Should().Be("PZA");
        l1.Descripcion.Should().Be("Servicio de prueba");
        l1.Cantidad.Should().Be(2m);
        l1.ValorUnitario.Should().Be(500.00m);
        l1.Importe.Should().Be(1000.00m);
        l1.Descuento.Should().BeNull();
    }

    [Fact]
    public void GenerarXmlStream_produce_stream_legible_desde_el_inicio()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor SA"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet SA"),
            Lineas:
            [
                new LineaCfdiPrueba(
                    Posicion: 1,
                    Cantidad: 1m,
                    PrecioUnitario: 100m,
                    ArticuloNombre: "Articulo Stream")
            ]);

        using var stream = _generador.GenerarXmlStream(datos);

        stream.Position.Should().Be(0);
        var parseado = _parser.Parsear(stream);
        parseado.Total.Should().Be(116.00m);
        parseado.Lineas.Should().HaveCount(1);
        parseado.Lineas[0].Descripcion.Should().Be("Articulo Stream");
    }

    [Fact]
    public void GenerarXml_multiples_lineas_con_descuentos_y_retencion_isr()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor SA"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet SA", CodigoPostal: "97300"),
            Lineas:
            [
                new LineaCfdiPrueba(
                    Posicion: 1,
                    Cantidad: 2m,
                    PrecioUnitario: 1000m,
                    ArticuloClave: "VIDRIO-01",
                    ArticuloNombre: "Vidrio Templado",
                    Descuento: 200m,
                    TasaIva: 0.16m),
                new LineaCfdiPrueba(
                    Posicion: 2,
                    Cantidad: 1m,
                    PrecioUnitario: 500m,
                    ArticuloClave: "FLETE-01",
                    ArticuloNombre: "Flete Foraneo",
                    Descuento: 0m,
                    TasaIva: 0.16m,
                    RetencionIsr: 50m,
                    TasaRetencionIsr: 0.10m)
            ],
            MetodoPago: "PPD");

        var xml = _generador.GenerarXml(datos);
        var parseado = _parser.Parsear(xml);

        // Linea 1: Importe = 2000.00, Descuento = 200.00, Base = 1800.00, IVA 16% = 288.00
        // Linea 2: Importe = 500.00, Descuento = 0.00, Base = 500.00, IVA 16% = 80.00, Retencion ISR = 50.00
        // Subtotal = 2500.00
        // Descuentos = 200.00
        // IVA Trasladado = 368.00
        // Retenciones = 50.00
        // Total = 2500.00 - 200.00 + 368.00 - 50.00 = 2618.00
        parseado.Subtotal.Should().Be(2500.00m);
        parseado.Descuentos.Should().Be(200.00m);
        parseado.ImpuestosTrasladados.Should().Be(368.00m);
        parseado.Retenciones.Should().Be(50.00m);
        parseado.Total.Should().Be(2618.00m);

        parseado.RetencionesDetalle.Should().NotBeNull();
        parseado.RetencionesDetalle.Should().HaveCount(1);
        var ret = parseado.RetencionesDetalle![0];
        ret.Impuesto.Should().Be("001");
        ret.Importe.Should().Be(50.00m);
        ret.Base.Should().Be(500.00m);
        ret.Tasa.Should().Be(0.10m);

        parseado.Lineas.Should().HaveCount(2);
        parseado.Lineas[0].Descuento.Should().Be(200.00m);
        parseado.Lineas[1].Descuento.Should().BeNull();
    }

    [Fact]
    public void GenerarXml_moneda_usd_incluye_tipo_cambio()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "US Glass Corp"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet SA"),
            Lineas:
            [
                new LineaCfdiPrueba(
                    Posicion: 1,
                    Cantidad: 10m,
                    PrecioUnitario: 12.50m,
                    ArticuloNombre: "Herraje Importado")
            ],
            Moneda: "USD",
            TipoCambio: 18.5432m);

        var xml = _generador.GenerarXml(datos);
        var parseado = _parser.Parsear(xml);

        parseado.Moneda.Should().Be("USD");
        parseado.TipoCambio.Should().Be(18.5432m);
        parseado.Subtotal.Should().Be(125.00m);
        parseado.Total.Should().Be(145.00m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerarXml_sin_rfc_emisor_lanza_excepcion(string? rfcInvalido)
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba(rfcInvalido!, "Proveedor"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet"),
            Lineas: [new LineaCfdiPrueba(1, 1m, 100m)]);

        var act = () => _generador.GenerarXml(datos);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*RFC*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerarXml_sin_rfc_receptor_lanza_excepcion(string? rfcInvalido)
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor"),
            Receptor: new ReceptorCfdiPrueba(rfcInvalido!, "Millet"),
            Lineas: [new LineaCfdiPrueba(1, 1m, 100m)]);

        var act = () => _generador.GenerarXml(datos);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*receptor*");
    }

    [Fact]
    public void GenerarXml_lineas_vacias_lanza_excepcion()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet"),
            Lineas: []);

        var act = () => _generador.GenerarXml(datos);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerarXml_lineas_con_suma_menor_o_igual_a_cero_lanza_excepcion()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet"),
            Lineas:
            [
                new LineaCfdiPrueba(1, 0m, 100m),
                new LineaCfdiPrueba(2, 0m, 200m)
            ]);

        var act = () => _generador.GenerarXml(datos);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerarXml_linea_con_cantidad_negativa_lanza_excepcion()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet"),
            Lineas:
            [
                new LineaCfdiPrueba(1, -5m, 100m)
            ]);

        var act = () => _generador.GenerarXml(datos);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerarXml_valores_por_omision_uuid_folio_fecha_serie()
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet"),
            Lineas: [new LineaCfdiPrueba(1, 1m, 100m)]);

        var xml = _generador.GenerarXml(datos);
        var parseado = _parser.Parsear(xml);

        parseado.Serie.Should().Be("PRUEBA");
        parseado.Folio.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(parseado.UuidCfdi, out _).Should().BeTrue();
        parseado.FechaCfdi.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("PUE", "03")]
    [InlineData("PPD", "99")]
    public void GenerarXml_forma_pago_se_deriva_correctamente_segun_metodo_pago(string metodoPago, string formaPagoEsperada)
    {
        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba("PROV010101AAA", "Proveedor"),
            Receptor: new ReceptorCfdiPrueba("MIL010101AAA", "Millet"),
            Lineas: [new LineaCfdiPrueba(1, 1m, 100m)],
            MetodoPago: metodoPago);

        var xml = _generador.GenerarXml(datos);

        xml.Should().Contain($"FormaPago=\"{formaPagoEsperada}\"");
        xml.Should().Contain($"MetodoPago=\"{metodoPago}\"");
    }
}
