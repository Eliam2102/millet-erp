using ClosedXML.Excel;
using Millet.ActivosFijos.Application;
using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.UnitTests;
public sealed class ArchivoLibroTests
{
    private static byte[] Excel(Action<XLWorkbook> configurar)
    {
        using var libro = new XLWorkbook(); configurar(libro);
        using var stream = new MemoryStream(); libro.SaveAs(stream); return stream.ToArray();
    }
    [Fact] public void Excel_lee_primera_hoja_con_datos_alias_fechas_importes_y_fila_real()
    {
        var bytes = Excel(w =>
        {
            w.AddWorksheet("Vacía"); var s = w.AddWorksheet("SAP");
            string[] headers = ["Núm. activo", "Tipo de activo", "Descripción", "Fecha de compra", "Valor", "Dep. acumulada", "Sucursal", "Ubicación", "Extra"];
            for (var i = 0; i < headers.Length; i++) s.Cell(3, i + 1).Value = headers[i];
            s.Cell(5, 1).Value = "000123"; s.Cell(5, 2).Value = "Maquinaria y equipo"; s.Cell(5, 3).Value = "Equipo SAP";
            s.Cell(5, 4).Value = new DateTime(2024, 3, 5); s.Cell(5, 5).Value = "$500,000.00"; s.Cell(5, 6).Value = 100000;
            s.Cell(5, 7).Value = "CONKAL"; s.Cell(5, 8).Value = "Planta";
            s.Cell(100, 20).Style.Fill.BackgroundColor = XLColor.Red;
        });
        var tabla = ArchivoLibro.Leer(bytes, "sap.xlsx");
        Assert.Empty(tabla.Errores); Assert.Single(tabla.Avisos); var fila = Assert.Single(tabla.Filas);
        Assert.Equal(5, fila.Numero); Assert.Equal("000123", fila.Valores[0]); Assert.Equal(new DateOnly(2024, 3, 5), ArchivoLibro.Fecha(fila.Valores[3]));
        Assert.Equal(500000, ArchivoLibro.Monto(fila.Valores[5])); Assert.Equal(100000, ArchivoLibro.Monto(fila.Valores[6]));
        Assert.Single(ArchivoLibro.Leer(bytes, "sap.xlsx", "SAP").Filas);
        Assert.Throws<BusinessRuleException>(() => ArchivoLibro.Leer(bytes, "sap.xlsx", "No existe"));
    }
    [Fact] public void Columnas_faltantes_duplicadas_y_desconocidas_se_reportan()
    {
        var tabla = ArchivoLibro.LeerCsv("numero,clase,descripcion,fecha de compra,dep. acumulada,extra\n1,153,Equipo,05/03/2024,0,x");
        var error = Assert.Single(tabla.Errores); Assert.Equal("valor", error.Columna); Assert.Contains("Falta valor", error.Mensaje); Assert.Contains("APC", error.Mensaje);
        Assert.Single(tabla.Avisos);
        Assert.Contains(ArchivoLibro.LeerCsv(CargaLibroServicio.Plantilla.Trim() + ",costo\n").Errores, e => e.Codigo == "AF_COLUMNA_DUPLICADA");
    }
    [Theory] [InlineData("05/03/2024", 2024, 3, 5)] [InlineData("2024-03-05", 2024, 3, 5)]
    public void Fechas_siempre_dia_mes(string texto, int ano, int mes, int dia) => Assert.Equal(new DateOnly(ano, mes, dia), ArchivoLibro.Fecha(texto));
    [Theory] [InlineData("03/25/2024")] [InlineData("03/04/24")] [InlineData("2024/03/05")]
    public void Rechaza_fechas_fuera_del_contrato(string texto) => Assert.Throws<FormatException>(() => ArchivoLibro.Fecha(texto));
    [Theory] [InlineData("$500,000.00", 500000)] [InlineData("1,234.56", 1234.56)] [InlineData("0", 0)]
    public void Montos(string texto, decimal esperado) => Assert.Equal(esperado, ArchivoLibro.Monto(texto));
    [Theory] [InlineData("1,23")] [InlineData("1.234,56")]
    public void Rechaza_agrupacion_ambigua(string texto) => Assert.Throws<FormatException>(() => ArchivoLibro.Monto(texto));
    [Fact] public void Csv_anterior_y_filas_vacias_siguen_funcionando()
    {
        var tabla = ArchivoLibro.LeerCsv("\uFEFF" + CargaLibroServicio.Plantilla + "\nSAP-01,153,Equipo,2024-01-01,2024-01-01,100,0,,,,,,no,,no\n,,,,,,,,,,,,,,\n");
        Assert.Empty(tabla.Errores); Assert.Empty(tabla.Avisos); Assert.Equal(3, Assert.Single(tabla.Filas).Numero);
    }
    [Fact] public void Plantilla_tiene_instrucciones_catalogo_y_ejemplo()
    {
        using var stream = new MemoryStream(ArchivoLibro.PlantillaExcel()); using var libro = new XLWorkbook(stream);
        Assert.Equal("EJEMPLO", libro.Worksheet("Activos").Cell(2, 1).GetString());
        Assert.Equal(CatalogoClasesMillet.Clases.Count, libro.Worksheet("Instrucciones").RowsUsed().Count(r => r.Cell(1).GetString().Length == 3));
        Assert.Empty(ArchivoLibro.Leer(stream.ToArray(), "plantilla.xlsx").Errores);
    }
    [Fact] public void Rechaza_extension_contenido_y_tamano_invalidos()
    {
        Assert.Throws<BusinessRuleException>(() => ArchivoLibro.Leer([1, 2, 3], "sap.xlsx"));
        Assert.Throws<BusinessRuleException>(() => ArchivoLibro.Leer([1], "sap.xls"));
        Assert.Throws<BusinessRuleException>(() => ArchivoLibro.Leer(new byte[ArchivoLibro.LimiteBytes + 1], "sap.csv"));
    }
    [Fact] public void Saldo_inicial_admite_custodia_opcional_sin_relajar_capitalizacion()
    {
        var activo = new Activo(Guid.NewGuid(), Guid.NewGuid(), "SAP-123", new("Equipo", Guid.NewGuid(), null, null, null,
            new(2024, 3, 5), new(2024, 3, 5), 500000, "MXN", 1, new(Guid.Empty, Guid.NewGuid(), "Planta", Guid.Empty, null), TipoImportacionActivo.No, null, false, "SAP:prueba"));
        Assert.Throws<BusinessRuleException>(() => activo.ValidarCapitalizacion());
        activo.CargarLibro(new(2026, 10, 31), 100000);
        Assert.Equal(400000, activo.ValorEnLibros); Assert.Equal(Guid.Empty, activo.ResponsableId); Assert.Null(activo.PolizaAltaId);
        Assert.Throws<BusinessRuleException>(() => activo.CargarLibro(new(2026, 10, 31), 100000));
    }
}
