using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ActivosFijos.Application;

public sealed record FilaLibroArchivo(int Numero, string[] Valores);
public sealed record TablaLibroArchivo(IReadOnlyList<FilaLibroArchivo> Filas, IReadOnlyList<ErrorCarga> Errores, IReadOnlyList<ErrorCarga> Avisos, string? Hoja = null);

public static class ArchivoLibro
{
    public const int LimiteBytes = 10 * 1024 * 1024;
    public static readonly string[][] Columnas =
    [
        ["número", "núm. activo", "código", "clave", "ItemCode"],
        ["clase", "tipo", "tipo de activo", "grupo"],
        ["descripción", "nombre", "ItemName"],
        ["fecha de adquisición", "fecha de compra", "capitalización", "fechaAdquisicion"],
        ["inicio de uso", "fecha de alta", "inicioUso"],
        ["valor", "valor original", "costo", "APC", "valorOriginal"],
        ["depreciación acumulada", "dep. acumulada", "depreciacionAcumulada"],
        ["responsable", "responsableId"], ["sucursal", "sucursalId"], ["ubicación"],
        ["centro de costo", "CeCo", "centroCostoId"], ["equipo", "equipoId"],
        ["importado"], ["pedimento"], ["arrendado"]
    ];
    public static string Normalizar(string texto) => string.Concat(texto.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))).ToLowerInvariant();

    public static DateOnly Fecha(string valor) => DateOnly.TryParseExact(valor, ["yyyy-MM-dd", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha)
        ? fecha : throw new FormatException("Fecha inválida. Use dd/mm/aaaa o aaaa-mm-dd; no se acepta mm/dd/aaaa.");
    public static decimal Monto(string valor)
    {
        var texto = valor.Trim().TrimStart('$').Trim();
        if (!Regex.IsMatch(texto, @"^-?(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?$", RegexOptions.CultureInvariant))
            throw new FormatException("Importe inválido. Use 1234.56 o $1,234.56.");
        return decimal.Parse(texto, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    public static TablaLibroArchivo LeerCsv(string csv)
    {
        var filas = new List<FilaLibroArchivo>();
        var texto = csv.TrimStart('\uFEFF');
        var lineas = texto.Split('\n');
        using var reader = new TextFieldParser(new StringReader(texto));
        reader.SetDelimiters(","); reader.HasFieldsEnclosedInQuotes = true;
        while (!reader.EndOfData)
        {
            var numero = (int)reader.LineNumber;
            while (numero <= lineas.Length && string.IsNullOrWhiteSpace(lineas[numero - 1])) numero++;
            try { filas.Add(new(numero, reader.ReadFields()!)); }
            catch (MalformedLineException) { return new([], [new(numero, "AF_FORMATO_INVALIDO", "El renglón CSV está mal formado.")], []); }
            if (filas.Count > 20001) throw Invalido("Máximo 20,000 activos por archivo.");
        }
        return Mapear(filas);
    }

    public static TablaLibroArchivo Leer(byte[] contenido, string nombre, string? hoja = null)
    {
        if (contenido.Length == 0 || contenido.Length > LimiteBytes) throw Invalido("El archivo debe contener datos y pesar hasta 10 MB.");
        var extension = Path.GetExtension(nombre).ToLowerInvariant();
        if (extension == ".csv")
        {
            try
            {
                var texto = new UTF8Encoding(false, true).GetString(contenido);
                if (texto.Contains('\0')) throw Invalido("El CSV debe ser texto UTF-8.");
                return LeerCsv(texto);
            }
            catch (DecoderFallbackException) { throw Invalido("El CSV debe ser texto UTF-8."); }
        }
        if (extension != ".xlsx") throw Invalido("Seleccione un archivo .xlsx o .csv.");
        try
        {
            using var stream = new MemoryStream(contenido);
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
            {
                if (zip.Entries.Sum(e => e.Length) > 50 * 1024 * 1024 || zip.Entries.Count > 2000)
                    throw Invalido("El contenido descomprimido del Excel excede el límite permitido.");
            }
            stream.Position = 0;
            using var libro = new XLWorkbook(stream);
            var sheet = string.IsNullOrWhiteSpace(hoja)
                ? libro.Worksheets.FirstOrDefault(s => s.FirstRowUsed(XLCellsUsedOptions.Contents) != null)
                : libro.Worksheets.FirstOrDefault(s => s.Name.Equals(hoja, StringComparison.OrdinalIgnoreCase));
            if (sheet == null) throw Invalido("No existe la hoja solicitada o el libro está vacío.");
            var rango = sheet.RangeUsed(XLCellsUsedOptions.Contents);
            if (rango == null) return Mapear([]);
            if (rango.RowCount() > 20001 || rango.ColumnCount() > 200) throw Invalido("Máximo 20,000 activos y 200 columnas por hoja.");
            var filas = new List<FilaLibroArchivo>();
            foreach (var row in rango.Rows())
            {
                if (row.IsEmpty(XLCellsUsedOptions.Contents)) continue;
                filas.Add(new(row.RangeAddress.FirstAddress.RowNumber, row.Cells().Select(c => c.HasFormula ? "[Fórmula: sustituya por su valor]" : c.DataType switch
                {
                    XLDataType.DateTime => c.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    XLDataType.Number => c.GetDouble().ToString("G", CultureInfo.InvariantCulture),
                    _ => c.GetString().Trim()
                }).ToArray()));
            }
            return Mapear(filas) with { Hoja = sheet.Name };
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or FormatException or System.Xml.XmlException)
        { throw Invalido("No se puede leer el archivo Excel. Revise que sea un .xlsx válido y no esté protegido."); }
    }

    private static TablaLibroArchivo Mapear(IReadOnlyList<FilaLibroArchivo> originales)
    {
        var filas = originales.Where(f => f.Valores.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
        if (filas.Count == 0) return new([], [new(1, "AF_ARCHIVO_VACIO", "Agregue encabezados y al menos un activo.")], []);
        var errores = new List<ErrorCarga>(); var avisos = new List<ErrorCarga>();
        var indices = Enumerable.Repeat(-1, Columnas.Length).ToArray();
        var encabezado = filas[0];
        for (var i = 0; i < encabezado.Valores.Length; i++)
        {
            var texto = encabezado.Valores[i];
            if (string.IsNullOrWhiteSpace(texto)) continue;
            var columna = Array.FindIndex(Columnas, alias => alias.Any(a => Normalizar(a) == Normalizar(texto)));
            if (columna < 0) avisos.Add(new(encabezado.Numero, "AF_COLUMNA_IGNORADA", $"Se ignora la columna «{texto}».", texto));
            else if (indices[columna] >= 0) errores.Add(new(encabezado.Numero, "AF_COLUMNA_DUPLICADA", $"La columna {Columnas[columna][0]} está repetida.", texto));
            else indices[columna] = i;
        }
        foreach (var i in new[] { 0, 1, 2, 3, 5, 6 })
            if (indices[i] < 0) errores.Add(new(encabezado.Numero, "AF_COLUMNA_FALTANTE", $"Falta {Columnas[i][0]}. Nombres aceptados: {string.Join(", ", Columnas[i])}.", Columnas[i][0]));
        var mapeadas = new List<FilaLibroArchivo>();
        foreach (var fila in filas.Skip(1))
        {
            if (fila.Valores.Skip(encabezado.Valores.Length).Any(v => !string.IsNullOrWhiteSpace(v)))
                errores.Add(new(fila.Numero, "AF_COLUMNAS_INVALIDAS", "Hay valores sin encabezado. Encierre entre comillas los importes CSV que contengan comas."));
            var valores = indices.Select(i => i >= 0 && i < fila.Valores.Length ? fila.Valores[i].Trim() : "").ToArray();
            for (var i = 0; i < valores.Length; i++)
                if (valores[i] == "[Fórmula: sustituya por su valor]")
                    errores.Add(new(fila.Numero, "AF_FORMULA_NO_ADMITIDA", "Sustituya la fórmula por su valor antes de cargar.", Columnas[i][0]));
            mapeadas.Add(new(fila.Numero, valores));
        }
        return new(mapeadas, errores, avisos);
    }

    public static byte[] PlantillaExcel()
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Activos");
        for (var i = 0; i < Columnas.Length; i++) hoja.Cell(1, i + 1).Value = Columnas[i][0];
        string[] ejemplo = ["EJEMPLO", "153", "EJEMPLO · sustituir esta fila", "2024-03-05", "", "500000", "100000", "", "CLAVE DE SUCURSAL", "Planta", "", "", "no", "", "no"];
        for (var i = 0; i < ejemplo.Length; i++) hoja.Cell(2, i + 1).Value = ejemplo[i];
        var instrucciones = libro.AddWorksheet("Instrucciones");
        instrucciones.Cell(1, 1).Value = "Elimine la fila EJEMPLO antes de cargar. Corte, versión y totales se capturan en la pantalla. La carga no crea póliza.";
        string[] ayuda = ["Número SAP único, se conserva como texto.", "Clave o nombre del catálogo listado abajo.", "Descripción del activo.", "Fecha Excel, dd/mm/aaaa o aaaa-mm-dd; nunca mm/dd.", "Opcional; se usa adquisición si falta.", "MXN: número o texto $1,234.56.", "MXN: entre cero y valor original.", "Opcional: correo o nombre de usuario. Sin coincidencia: aviso y sin responsable.", "Clave o nombre de sucursal activa. Necesaria para asignación y permisos.", "Ubicación física (texto).", "Opcional: código de centro de costo activo.", "Opcional: clave de equipo activo del centro indicado.", "Opcional: no, temporal o definitivo; vacío = no.", "Obligatorio para importación temporal o definitiva.", "Opcional: sí/no; vacío = no."];
        for (var i = 0; i < Columnas.Length; i++)
        {
            instrucciones.Cell(i + 3, 1).Value = Columnas[i][0];
            instrucciones.Cell(i + 3, 2).Value = ayuda[i];
            instrucciones.Cell(i + 3, 3).Value = string.Join(", ", Columnas[i]);
        }
        var fila = 20;
        foreach (var clase in CatalogoClasesMillet.Clases)
        { instrucciones.Cell(fila, 1).Value = clase.Clave; instrucciones.Cell(fila++, 2).Value = clase.Nombre; }
        hoja.Columns().AdjustToContents(); instrucciones.Columns().AdjustToContents(10, 90);
        using var stream = new MemoryStream(); libro.SaveAs(stream); return stream.ToArray();
    }
    private static BusinessRuleException Invalido(string mensaje) => new("AF_ARCHIVO_INVALIDO", mensaje);
}
