using System.Text.Json;
using Millet.Facturacion.Application.Timbrado;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Pdf;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Facturas;

/// <summary>
/// 86e3tfdw2 — observaciones de la factura de venta: la nota que escribe el
/// cajero (prellenada con los anticipos aplicados) se imprime en el PDF y
/// NUNCA viaja al XML del CFDI.
/// </summary>
public sealed class ObservacionesFacturaTests
{
    private const string Nota = "Anticipos aplicados: FANT-12 $5,000.00 MXN";

    private static FacturaVenta Factura()
    {
        var f = FacturaVenta.CrearBorrador(
            Guid.NewGuid(), "FA-000001", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("BBB010101BBB", "Cliente SA", "601", "64000", "G03", "MEX", false),
            new DatosFiscalesEmisor("AAA010101AAA", "Millet", "601", "76120"), "PUE", "01", "MXN", null, 2026, 10,
            (short)1, ComportamientoFiscal.ConAnticipo, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Vidrio templado", "H87", 2m, 100m, 0m, "02", 0.16m, null, null);
        f.RecalcularTotales();
        return f;
    }

    // ---- Dominio ----

    [Fact]
    public void FijarObservaciones_guarda_el_texto_recortado()
    {
        var f = Factura();

        f.FijarObservaciones("  " + Nota + "  ");

        f.Observaciones.Should().Be(Nota);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FijarObservaciones_vacia_deja_null(string? texto)
    {
        var f = Factura();
        f.FijarObservaciones(Nota);

        f.FijarObservaciones(texto);

        f.Observaciones.Should().BeNull();
    }

    [Fact]
    public void FijarObservaciones_rechaza_mas_de_1000_caracteres()
    {
        var f = Factura();

        var act = () => f.FijarObservaciones(new string('x', FacturaVenta.ObservacionesMaxLength + 1));

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_OBSERVACIONES_LARGAS");
        f.FijarObservaciones(new string('x', FacturaVenta.ObservacionesMaxLength));
        f.Observaciones.Should().HaveLength(1000);
    }

    [Fact]
    public void FijarObservaciones_rechaza_factura_timbrada()
    {
        var f = Factura();
        f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado("UUID-1", "sc", "ss", "20001000000300022815", DateTimeOffset.UtcNow, "SAT970701NN3", Guid.NewGuid());

        var act = () => f.FijarObservaciones(Nota);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_INMUTABLE");
    }

    // ---- PDF ----

    [Fact]
    public void Pdf_incluye_bloque_Observaciones_cuando_hay_texto()
    {
        var f = Factura();
        f.FijarObservaciones(Nota);

        QuestPdfFacturaGenerator.BloqueObservaciones(f)
            .Should().Be(("Observaciones / Remarks", Nota));
    }

    [Fact]
    public void Pdf_omite_bloque_Observaciones_sin_texto()
    {
        QuestPdfFacturaGenerator.BloqueObservaciones(Factura()).Should().BeNull();
    }

    [Theory]
    [InlineData(FormatoPdfFactura.Bilingue)]
    [InlineData(FormatoPdfFactura.TermicaSimplificada)]
    public void Pdf_con_observaciones_se_genera_y_crece(FormatoPdfFactura formato)
    {
        var gen = new QuestPdfFacturaGenerator();
        var sin = gen.Generar(Factura(), formato).Contenido.Length;

        var f = Factura();
        f.FijarObservaciones(Nota);
        var con = gen.Generar(f, formato).Contenido.Length;

        con.Should().BeGreaterThan(sin);
    }

    // ---- CFDI (XML) ----

    [Fact]
    public void Cfdi_no_incluye_observaciones()
    {
        var f = Factura();
        f.FijarObservaciones(Nota);

        var emision = CfdiEmisionBuilder.DesdeFacturaVenta(f, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(emision);
        json.Should().NotContain("FANT-12");
        json.Should().NotContain("Anticipos aplicados");
        json.Should().NotContainEquivalentOf("observaciones");
    }

    // ---- Nota prellenada ----

    [Fact]
    public void NotaAnticipos_compone_folios_e_importes_en_formato_es_MX()
    {
        var nota = NotaAnticiposAplicados.Componer(
        [
            ("FANT-12", 5000m, "MXN"),
            ("FANT-15", 1200m, "MXN"),
        ]);

        nota.Should().Be("Anticipos aplicados: FANT-12 $5,000.00 MXN; FANT-15 $1,200.00 MXN");
    }

    [Fact]
    public void NotaAnticipos_redondea_a_dos_decimales_y_respeta_la_moneda()
    {
        var nota = NotaAnticiposAplicados.Componer([("FANT-1", 1234567.005m, "usd")]);

        nota.Should().Be("Anticipos aplicados: FANT-1 $1,234,567.01 USD");
    }

    [Fact]
    public void NotaAnticipos_sin_anticipos_es_vacia()
    {
        NotaAnticiposAplicados.Componer([]).Should().BeEmpty();
    }
}
