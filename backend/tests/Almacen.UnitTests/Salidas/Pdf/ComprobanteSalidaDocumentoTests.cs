using System.Reflection;
using System.Text;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Salidas.Pdf;
using Millet.Almacen.Domain.Movimientos;
using Millet.Documentos;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Millet.Almacen.UnitTests.Salidas.Pdf;

/// <summary>Comprobante de salida con el formato M2 de Millet (86e3tferp).</summary>
public class ComprobanteSalidaDocumentoTests
{
    public ComprobanteSalidaDocumentoTests() => PdfLicencia.Inicializar();

    [Theory]
    [InlineData(3, 1)]
    [InlineData(80, 3)]
    public void Genera_un_pdf_que_pagina_los_renglones(int renglones, int paginas)
    {
        var impresion = Impresion(renglones);

        Encoding.ASCII.GetString(ComprobanteSalidaDocumento.Generar(impresion), 0, 4).Should().Be("%PDF");
        ComprobanteSalidaDocumento.Crear(impresion)
            .GenerateImages(new ImageGenerationSettings { RasterDpi = 36 }).Should().HaveCount(paginas);
    }

    [Fact]
    public void Sin_empresa_ni_nombres_el_pdf_sale_igual()
    {
        var impresion = Impresion(1) with { Empresa = null, Elaboro = null, Recibio = null, FechaContabilizado = null };

        Encoding.ASCII.GetString(ComprobanteSalidaDocumento.Generar(impresion), 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public void Comentarios_con_rq_llevan_folio_nota_y_quien_recibe()
    {
        var s = Salida(esPorVale: false) with { RqFolio = "C00195865", Observaciones = "AC", PersonaDestinatariaNombre = "Julio Tuyub" };

        ComprobanteSalidaPdfHandler.Comentarios(s).Should().Be("RQ: C00195865 AC SE ENTREGA A JULIO TUYUB");
    }

    [Fact]
    public void Comentarios_de_vale_dicen_vale_y_la_rq_que_lo_regulariza()
    {
        ComprobanteSalidaPdfHandler.Comentarios(Salida(esPorVale: true)).Should().Be("VALE URGENTE");
        ComprobanteSalidaPdfHandler.Comentarios(Salida(esPorVale: true) with { RqRegularizadoraFolio = "C00195900" })
            .Should().Be("VALE URGENTE · RQ: C00195900");
    }

    private static ComprobanteSalidaImpresion Impresion(int renglones)
    {
        using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo-millet.jpg")!;
        using var logo = new MemoryStream();
        recurso.CopyTo(logo);
        var empresa = new EmpresaEncabezado("M INDUSTRIA, S.A. DE C.V.", "MILLET", "XAXX010101000", "601",
            ["AV. LOPEZ PORTILLO MANZANA 46 LOTE 1", "CANCUN", "77524, QR"], logo.ToArray());
        return new ComprobanteSalidaImpresion(empresa, "SAL-000123", new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 28),
            Enumerable.Range(1, renglones).Select(i => new RenglonSalidaImpresion(i, $"IPP{60000 + i}", $"CINTA DE PRUEBA {i}", "PZA", 15m)).ToList(),
            "RQ: C00195865 SE ENTREGA A PERSONA DE PRUEBA", "Persona que elabora", "Persona que recibe");
    }

    private static SalidaDetalle Salida(bool esPorVale) => new(
        Guid.NewGuid(), "SAL-1", new DateOnly(2026, 10, 28), Guid.NewGuid(), null, null, null, null, esPorVale, null,
        null, null, EstadoMovimiento.Registrado, 1, null, null, null, null, null, []);
}
