using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.UnitTests.Adjuntos;

/// <summary>
/// Pruebas unitarias para la política de adjuntos (F1-ADM-11, formato y tamaño de prueba).
/// </summary>
public class AdjuntosPoliticaOptionsTests
{
    private static readonly byte[] CabeceraPdfValida = "%PDF-1.4"u8.ToArray();
    private static readonly byte[] CabeceraPngValida = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] CabeceraJpgValida = [0xFF, 0xD8, 0xFF, 0xE0];
    private static readonly byte[] CabeceraExeMz = [0x4D, 0x5A, 0x90, 0x00]; // MZ header

    [Fact]
    public void ArchivoPermitido_PdfConFirmaValida_NoLanza()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "factura_123.pdf",
            contentType: "application/pdf",
            longitud: 1024 * 1024,
            cabecera: CabeceraPdfValida);

        act.Should().NotThrow();
    }

    [Fact]
    public void ArchivoPermitido_MetodoEstaticoValidar_NoLanza()
    {
        var act = () => AdjuntosPoliticaOptions.Validar(
            nombreArchivo: "orden.pdf",
            contentType: "application/pdf",
            longitud: 500,
            cabecera: CabeceraPdfValida);

        act.Should().NotThrow();
    }

    [Fact]
    public void ExtensionNoPermitida_Exe_LanzaFormatoNoPermitido()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "malware.exe",
            contentType: "application/octet-stream",
            longitud: 1024,
            cabecera: CabeceraExeMz);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
        ex.Message.Should().Contain(".exe");
    }

    [Fact]
    public void MimeNoPermitido_LanzaFormatoNoPermitido()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "documento.pdf",
            contentType: "application/x-msdownload",
            longitud: 1024,
            cabecera: CabeceraPdfValida);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
        ex.Message.Should().Contain("application/x-msdownload");
    }

    [Fact]
    public void TamanoExactoAlMaximo_NoLanza()
    {
        var opciones = new AdjuntosPoliticaOptions { MaxBytes = 1000 };

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "documento.pdf",
            contentType: "application/pdf",
            longitud: 1000,
            cabecera: CabeceraPdfValida);

        act.Should().NotThrow();
    }

    [Fact]
    public void TamanoExcedidoEnUnByte_LanzaTamanoExcedido()
    {
        var opciones = new AdjuntosPoliticaOptions { MaxBytes = 1000 };

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "documento.pdf",
            contentType: "application/pdf",
            longitud: 1001,
            cabecera: CabeceraPdfValida);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("ADJUNTO_TAMANO_EXCEDIDO");
    }

    [Fact]
    public void PdfConFirmaDeExe_LanzaFormatoNoPermitido()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "falso.pdf",
            contentType: "application/pdf",
            longitud: 1024,
            cabecera: CabeceraExeMz);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
        ex.Message.Should().Contain("PDF");
    }

    [Theory]
    [InlineData(@"..\..\etc\archivo.pdf")]
    [InlineData(@"subdir/otra_carpeta/archivo.pdf")]
    [InlineData(@"C:\Users\Secret\archivo.pdf")]
    public void NombreConRuta_LimpiaNombreYValidaExtensionCorrectamente(string nombreConRuta)
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: nombreConRuta,
            contentType: "application/pdf",
            longitud: 2048,
            cabecera: CabeceraPdfValida);

        act.Should().NotThrow();
    }

    [Fact]
    public void PngValido_ConFirmaCorrecta_NoLanza()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "evidencia.png",
            contentType: "image/png",
            longitud: 2048,
            cabecera: CabeceraPngValida);

        act.Should().NotThrow();
    }

    [Fact]
    public void PngConFirmaInvalida_LanzaFormatoNoPermitido()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "corrupto.png",
            contentType: "image/png",
            longitud: 2048,
            cabecera: [0x00, 0x01, 0x02, 0x03]);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
        ex.Message.Should().Contain("PNG");
    }

    [Fact]
    public void JpgValido_ConFirmaCorrecta_NoLanza()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "foto.jpg",
            contentType: "image/jpeg",
            longitud: 2048,
            cabecera: CabeceraJpgValida);

        act.Should().NotThrow();
    }

    [Fact]
    public void JpgConFirmaInvalida_LanzaFormatoNoPermitido()
    {
        var opciones = new AdjuntosPoliticaOptions();

        var act = () => opciones.ValidarInstancia(
            nombreArchivo: "foto.jpeg",
            contentType: "image/jpeg",
            longitud: 2048,
            cabecera: [0x00, 0x00, 0x00]);

        var ex = act.Should().Throw<BusinessRuleException>().Which;
        ex.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
        ex.Message.Should().Contain("JPEG");
    }

    [Fact]
    public void OfficeYTxt_SinFirmaExigida_ConfianEnExtensionYMime()
    {
        var opciones = new AdjuntosPoliticaOptions();

        // txt con cualquier byte
        var actTxt = () => opciones.ValidarInstancia(
            nombreArchivo: "notas.txt",
            contentType: "text/plain",
            longitud: 120,
            cabecera: "Texto plano"u8);
        actTxt.Should().NotThrow();

        // xlsx
        var actXlsx = () => opciones.ValidarInstancia(
            nombreArchivo: "reporte.xlsx",
            contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            longitud: 5000,
            cabecera: [0x50, 0x4B, 0x03, 0x04]); // PK zip header
        actXlsx.Should().NotThrow();
    }

    private static AdjuntosPoliticaOptions PoliticaProveedor() => new AdjuntosPoliticaOptions
    {
        PorTipoEntidad =
        {
            ["proveedor"] = new AdjuntosPoliticaEntidadOptions
            {
                MaxBytes = 10 * 1024 * 1024,
                Extensiones = [".pdf", ".xml", ".jpg", ".jpeg", ".png"],
                ContentTypes = ["application/pdf", "application/xml", "text/xml", "image/jpeg", "image/png"]
            }
        }
    }.ParaEntidad("proveedor");

    [Fact]
    public void Xml_ValidoConYSinBom_NoLanza()
    {
        var p = PoliticaProveedor();
        byte[] conBom = [0xEF, 0xBB, 0xBF, (byte)'<', (byte)'?', (byte)'x'];

        p.Invoking(x => x.ValidarInstancia("cfdi.xml", "text/xml", 100, "<?xml version=\"1.0\"?>"u8)).Should().NotThrow();
        p.Invoking(x => x.ValidarInstancia("cfdi.xml", "application/xml", 100, conBom)).Should().NotThrow();
    }

    [Fact]
    public void Xml_BinarioDisfrazado_Lanza()
    {
        var p = PoliticaProveedor();

        p.Invoking(x => x.ValidarInstancia("x.xml", "application/xml", 100, CabeceraExeMz))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_FORMATO_NO_PERMITIDO");
    }

    [Fact]
    public void Proveedor_ExcedeDiezMb_Lanza_PeroDefaultGlobalAceptaQuince()
    {
        var p = PoliticaProveedor();
        var global = new AdjuntosPoliticaOptions();

        p.Invoking(x => x.ValidarInstancia("a.pdf", "application/pdf", 15 * 1024 * 1024, CabeceraPdfValida))
            .Should().Throw<BusinessRuleException>().Which.Code.Should().Be("ADJUNTO_TAMANO_EXCEDIDO");
        global.Invoking(x => x.ValidarInstancia("a.pdf", "application/pdf", 15 * 1024 * 1024, CabeceraPdfValida))
            .Should().NotThrow();
    }

    [Fact]
    public void Proveedor_RechazaDocx_YEntidadSinOverrideUsaDefault()
    {
        var p = PoliticaProveedor();
        const string docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        p.Invoking(x => x.ValidarInstancia("a.docx", docx, 10, "PK"u8)).Should().Throw<BusinessRuleException>();
        new AdjuntosPoliticaOptions().ParaEntidad("oc").Invoking(x => x.ValidarInstancia("a.docx", docx, 10, "PK"u8))
            .Should().NotThrow();
    }
}
