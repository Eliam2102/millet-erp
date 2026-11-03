using System.Text;
using ClosedXML.Excel;
using Millet.DatosMaestros.Application.Cargas;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.UnitTests.DatosMaestros;

public sealed class ArchivoMaestrosTests
{
    private static readonly ColumnaCarga[] Columnas = [new("Clave", "Obligatoria", "item_code"), new("Nombre", "Obligatorio")];
    [Theory]
    [InlineData(",", false, false)]
    [InlineData(";", false, false)]
    [InlineData(",", true, false)]
    [InlineData(";", false, true)]
    public void Lee_csv_con_codificaciones_y_separadores(string separador, bool bom, bool windows)
    {
        var texto = $"item_code{separador}Nombre\r\nA01{separador}\"Artículo, con acento y ; separador\"\r\n";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var contenido = windows ? Encoding.GetEncoding(1252).GetBytes(texto) : Encoding.UTF8.GetBytes(texto);
        if (bom) contenido = new byte[] { 0xef, 0xbb, 0xbf }.Concat(contenido).ToArray();
        var tabla = ArchivoMaestros.Leer(contenido, "datos.csv", Columnas);
        Assert.Empty(tabla.Errores);
        Assert.Equal("Artículo, con acento y ; separador", Assert.Single(tabla.Filas).Valores["Nombre"]);
        Assert.Equal(2, tabla.Filas[0].Numero);
    }
    [Fact]
    public void Csv_de_diez_megabytes_con_espacios_al_final_se_lee()
    {
        var csv = "Clave,Nombre\n001,Prueba\n002,".PadRight(ArchivoMaestros.LimiteBytes, ' ');
        var tabla = ArchivoMaestros.Leer(Encoding.UTF8.GetBytes(csv), "datos.csv", Columnas);
        Assert.Equal("Prueba", tabla.Filas[0].Valores["Nombre"]);
    }
    [Fact]
    public void Csv_con_renglon_que_excede_buffer_devuelve_error_de_negocio()
    {
        var csv = "Clave,Nombre\n001,".PadRight(ArchivoMaestros.LimiteBytes, 'X');
        Assert.Throws<BusinessRuleException>(() => ArchivoMaestros.Leer(Encoding.UTF8.GetBytes(csv), "datos.csv", Columnas));
    }
    [Fact]
    public void Renglones_conservan_numero_fisico_tras_lineas_vacias()
    {
        var tabla = ArchivoMaestros.Leer(Encoding.UTF8.GetBytes("\n\nClave,Nombre\n\n001,Prueba"), "datos.csv", Columnas);
        Assert.Equal(5, Assert.Single(tabla.Filas).Numero);
    }
    [Fact]
    public void Lee_excel_y_rechaza_formulas()
    {
        using var libro = new XLWorkbook(); var hoja = libro.AddWorksheet("Datos");
        hoja.Cell(1, 1).Value = "Clave"; hoja.Cell(1, 2).Value = "Nombre";
        hoja.Cell(2, 1).Value = "001"; hoja.Cell(2, 2).FormulaA1 = "1+1";
        using var stream = new MemoryStream(); libro.SaveAs(stream);
        var tabla = ArchivoMaestros.Leer(stream.ToArray(), "datos.xlsx", Columnas);
        Assert.Equal("001", tabla.Filas[0].Valores["Clave"]);
        Assert.Equal("Nombre", Assert.Single(tabla.Errores).Columna);
    }
    [Fact]
    public void Plantilla_excel_incluye_instrucciones_y_columnas()
    {
        using var libro = new XLWorkbook(new MemoryStream(ArchivoMaestros.Plantilla(Columnas, true)));
        Assert.True(libro.Worksheets.Contains("Instrucciones"));
        Assert.Equal("Clave", libro.Worksheet("Datos").Cell(1, 1).GetString());
    }
    [Fact]
    public void Rechaza_archivo_grande_y_limite_de_filas()
    {
        Assert.Throws<BusinessRuleException>(() => ArchivoMaestros.Leer(new byte[ArchivoMaestros.LimiteBytes + 1], "datos.csv", Columnas));
        var texto = "Clave,Nombre\n" + string.Concat(Enumerable.Repeat("A,B\n", 20001));
        Assert.Throws<BusinessRuleException>(() => ArchivoMaestros.Leer(Encoding.UTF8.GetBytes(texto), "datos.csv", Columnas));
    }
    [Fact]
    public void Detecta_encabezados_duplicados_y_desconocidos()
    {
        var tabla = ArchivoMaestros.Leer(Encoding.UTF8.GetBytes("Clave,item_code,Extra\nA,A,otro"), "datos.csv", Columnas);
        Assert.Contains(tabla.Errores, e => e.Codigo == "COLUMNA_DUPLICADA");
        Assert.Contains(tabla.Errores, e => e.Codigo == "COLUMNA_DESCONOCIDA");
    }
    [Fact]
    public void Conserva_salto_de_linea_y_comillas_en_csv()
    {
        var tabla = ArchivoMaestros.Leer(Encoding.UTF8.GetBytes("Clave,Nombre\n001,\"Una \"\"pieza\"\"\ncon descripción\""), "datos.csv", Columnas);
        Assert.Equal("Una \"pieza\"\ncon descripción", Assert.Single(tabla.Filas).Valores["Nombre"]);
    }
}
