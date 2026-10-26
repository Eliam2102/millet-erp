using Millet.Facturacion.Application.Proformas;

namespace Millet.Facturacion.UnitTests.Proformas;

/// <summary>
/// U1.8a CA-c: el comparador detecta qué cambió entre la última proforma y lo
/// que se va a timbrar (cantidad, tasa de IVA, RFC del receptor, conceptos
/// agregados/quitados y totales), con etiquetas legibles en español.
/// </summary>
public sealed class ProformaComparadorTests
{
    private static ProformaConcepto Concepto(int posicion, decimal cantidad = 2m, decimal? tasaIva = 0.16m,
        decimal valorUnitario = 100m) =>
        new(posicion, "30171500", "H87", $"Vidrio {posicion}", cantidad, valorUnitario,
            Math.Round(cantidad * valorUnitario, 2), 0m, "02",
            Math.Round(cantidad * valorUnitario * (tasaIva ?? 0m), 2), 0m, 0m, tasaIva);

    private static ProformaCfdi Proforma(params ProformaConcepto[] conceptos) => new(
        new ProformaEmisor("MIL010101AAA", "Millet", "601", "76120"),
        new ProformaReceptor("AAA010101AAA", "Cliente prueba", "601", "97000", "G03"),
        "PUE", "01", "MXN", null, "01", "Cfdi",
        conceptos,
        new ProformaTotales(
            conceptos.Sum(c => c.Importe), 0m, conceptos.Sum(c => c.Iva), 0m,
            conceptos.Sum(c => c.Importe) + conceptos.Sum(c => c.Iva)));

    [Fact]
    public void Misma_proforma_no_tiene_diferencias()
    {
        var p = Proforma(Concepto(1), Concepto(2));
        Assert.Empty(ProformaComparador.Comparar(p, p with { }));
    }

    [Fact]
    public void Detecta_cambio_de_cantidad_y_total()
    {
        var anterior = Proforma(Concepto(1), Concepto(2));
        var actual = Proforma(Concepto(1), Concepto(2, cantidad: 3m));

        var dif = ProformaComparador.Comparar(anterior, actual);

        var cantidad = Assert.Single(dif, d => d.Campo == "Concepto 2 · Cantidad");
        Assert.Equal("2", cantidad.Proforma);
        Assert.Equal("3", cantidad.Timbrado);
        var total = Assert.Single(dif, d => d.Campo == "Total");
        Assert.Equal("464.00", total.Proforma);
        Assert.Equal("580.00", total.Timbrado);
    }

    [Fact]
    public void Detecta_cambio_de_tasa_de_iva()
    {
        var dif = ProformaComparador.Comparar(Proforma(Concepto(1)), Proforma(Concepto(1, tasaIva: 0.08m)));

        var tasa = Assert.Single(dif, d => d.Campo == "Concepto 1 · Tasa IVA");
        Assert.Equal("0.16", tasa.Proforma);
        Assert.Equal("0.08", tasa.Timbrado);
        Assert.Contains(dif, d => d.Campo == "Concepto 1 · IVA");
        Assert.Contains(dif, d => d.Campo == "IVA trasladado");
    }

    [Fact]
    public void Detecta_cambio_de_rfc_del_receptor()
    {
        var anterior = Proforma(Concepto(1));
        var actual = anterior with { Receptor = anterior.Receptor with { Rfc = "BBB010101BBB" } };

        var d = Assert.Single(ProformaComparador.Comparar(anterior, actual));
        Assert.Equal("Receptor · RFC", d.Campo);
        Assert.Equal("AAA010101AAA", d.Proforma);
        Assert.Equal("BBB010101BBB", d.Timbrado);
    }

    [Fact]
    public void Detecta_concepto_agregado_y_concepto_quitado()
    {
        var uno = Proforma(Concepto(1));
        var dos = Proforma(Concepto(1), Concepto(2));

        var agregado = ProformaComparador.Comparar(uno, dos);
        var alta = Assert.Single(agregado, d => d.Campo == "Concepto 2");
        Assert.Null(alta.Proforma);
        Assert.Contains("Vidrio 2", alta.Timbrado);

        var quitado = ProformaComparador.Comparar(dos, uno);
        var baja = Assert.Single(quitado, d => d.Campo == "Concepto 2");
        Assert.Contains("Vidrio 2", baja.Proforma);
        Assert.Null(baja.Timbrado);
    }

    [Fact]
    public void Detecta_cambio_de_metodo_forma_y_tipo_de_cambio()
    {
        var anterior = Proforma(Concepto(1));
        var actual = anterior with { MetodoPago = "PPD", FormaPago = "99", Moneda = "USD", TipoCambio = 18.5m };

        var dif = ProformaComparador.Comparar(anterior, actual);

        Assert.Contains(dif, d => d.Campo == "Método de pago" && d.Proforma == "PUE" && d.Timbrado == "PPD");
        Assert.Contains(dif, d => d.Campo == "Forma de pago" && d.Proforma == "01" && d.Timbrado == "99");
        Assert.Contains(dif, d => d.Campo == "Moneda" && d.Timbrado == "USD");
        Assert.Contains(dif, d => d.Campo == "Tipo de cambio" && d.Proforma == null && d.Timbrado == "18.5");
    }

    [Fact]
    public void Tipo_de_cambio_que_la_proforma_no_fijaba_no_es_diferencia_en_la_misma_moneda()
    {
        var anterior = Proforma(Concepto(1)) with { Moneda = "USD", TipoCambio = null };
        var actual = anterior with { TipoCambio = 18.5m };

        var dif = ProformaComparador.Comparar(anterior, actual);

        Assert.DoesNotContain(dif, d => d.Campo == "Tipo de cambio");
    }
}
