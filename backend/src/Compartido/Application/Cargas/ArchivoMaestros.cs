using System.Globalization;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.DatosMaestros.Application.Cargas;

public sealed record ColumnaCarga(string Nombre, string Ayuda, params string[] Alias);
public sealed record FilaMaestros(int Numero, Dictionary<string, string> Valores);
public sealed record TablaMaestros(IReadOnlyList<FilaMaestros> Filas, IReadOnlyList<ErrorMaestros> Errores);
public sealed record ErrorMaestros(int Renglon, string Columna, string Codigo, string Mensaje);

public static class ArchivoMaestros
{
    public const int LimiteBytes = 10 * 1024 * 1024;
    public const int LimiteFilas = 20_000;
    public static string Normalizar(string texto) => string.Concat(texto.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))).ToLowerInvariant();
    public static TablaMaestros Leer(byte[] contenido, string nombre, IReadOnlyList<ColumnaCarga> columnas)
    {
        if (contenido.Length == 0 || contenido.Length > LimiteBytes) throw Invalido("El archivo debe contener datos y pesar hasta 10 MB.");
        var filas = new List<(int Numero, string[] Valores)>();
        if (Path.GetExtension(nombre).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            string texto;
            try { texto = new UTF8Encoding(false, true).GetString(contenido); }
            catch (DecoderFallbackException)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                texto = Encoding.GetEncoding(1252).GetString(contenido);
            }
            texto = texto.TrimStart('\uFEFF');
            if (texto.Contains('\0')) throw Invalido("El CSV debe estar en UTF-8 o Windows-1252.");
            var encabezado = texto.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var lineas = texto.Split('\n');
            using var lector = new TextFieldParser(new StringReader(texto.TrimEnd()));
            lector.SetDelimiters(encabezado.Count(c => c == ';') > encabezado.Count(c => c == ',') ? ";" : ",");
            lector.HasFieldsEnclosedInQuotes = true;
            try
            {
                while (!lector.EndOfData)
                {
                    var numero = checked((int)lector.LineNumber);
                    while (numero <= lineas.Length && string.IsNullOrWhiteSpace(lineas[numero - 1])) numero++;
                    filas.Add((numero, lector.ReadFields()!));
                    if (filas.Count > LimiteFilas + 1) throw Invalido("Máximo 20,000 renglones por archivo.");
                }
            }
            // shortcut: TextFieldParser limita el búfer de un renglón a ~10 millones de caracteres; usar lectura streaming si se requieren celdas de ese tamaño.
            catch (InvalidOperationException) { throw Invalido("El renglón CSV excede el tamaño que admite el lector. Divida su contenido en renglones más cortos."); }
            catch (MalformedLineException) { throw Invalido($"El renglón {lector.ErrorLineNumber} tiene comillas o separadores incorrectos."); }
        }
        else if (Path.GetExtension(nombre).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = new MemoryStream(contenido);
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
                    if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 50 * 1024 * 1024)
                        throw Invalido("El Excel descomprimido excede el límite permitido.");
                stream.Position = 0;
                using var libro = new XLWorkbook(stream);
                var hoja = libro.Worksheets.FirstOrDefault(h => !h.Name.Equals("Instrucciones", StringComparison.OrdinalIgnoreCase))
                    ?? throw Invalido("Agregue una hoja de datos.");
                var rango = hoja.RangeUsed(XLCellsUsedOptions.Contents) ?? throw Invalido("El archivo no contiene datos.");
                if (rango.RowCount() > LimiteFilas + 1 || rango.ColumnCount() > 200) throw Invalido("Máximo 20,000 renglones y 200 columnas.");
                foreach (var fila in rango.Rows())
                {
                    if (fila.IsEmpty(XLCellsUsedOptions.Contents)) continue;
                    filas.Add((fila.RowNumber(), fila.Cells().Select(c => c.HasFormula ? "[Fórmula]" : c.DataType switch
                    {
                        XLDataType.Number => c.GetDouble().ToString("G", CultureInfo.InvariantCulture),
                        XLDataType.DateTime => c.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        _ => c.GetString().Trim()
                    }).ToArray()));
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or FormatException or System.Xml.XmlException)
            { throw Invalido("No se puede leer el Excel. Seleccione un .xlsx válido y sin protección."); }
        }
        else throw Invalido("Seleccione un archivo .xlsx o .csv.");
        if (filas.Count < 2) throw Invalido("Agregue encabezados y al menos un renglón de datos.");
        var errores = new List<ErrorMaestros>();
        var mapa = new Dictionary<int, string>();
        var vistas = new HashSet<string>();
        for (var i = 0; i < filas[0].Valores.Length; i++)
        {
            var texto = filas[0].Valores[i];
            if (Normalizar(texto) is "error" or "renglonoriginal") continue;
            var columna = columnas.SingleOrDefault(c => c.Alias.Append(c.Nombre).Any(a => Normalizar(a) == Normalizar(texto)));
            if (columna is null) errores.Add(new(filas[0].Numero, texto, "COLUMNA_DESCONOCIDA", $"La columna «{texto}» no pertenece a esta plantilla. Descargue la plantilla del catálogo."));
            else if (!vistas.Add(columna.Nombre)) errores.Add(new(filas[0].Numero, texto, "COLUMNA_DUPLICADA", $"La columna «{columna.Nombre}» está repetida."));
            else mapa[i] = columna.Nombre;
        }
        var resultado = new List<FilaMaestros>();
        foreach (var (numero, valores) in filas.Skip(1))
        {
            var datos = columnas.ToDictionary(c => c.Nombre, _ => "");
            foreach (var (indice, columna) in mapa)
            {
                datos[columna] = indice < valores.Length ? valores[indice].Trim() : "";
                if (datos[columna] == "[Fórmula]") errores.Add(new(numero, columna, "FORMULA_NO_ADMITIDA", "Sustituya la fórmula por su valor antes de cargar."));
            }
            if (valores.Length > filas[0].Valores.Length) errores.Add(new(numero, "Archivo", "COLUMNAS_INVALIDAS", "Hay valores sin encabezado. Revise los separadores y las comillas."));
            resultado.Add(new(numero, datos));
        }
        return new(resultado, errores);
    }
    public static byte[] Plantilla(IReadOnlyList<ColumnaCarga> columnas, bool excel)
    {
        if (!excel) return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join(',', columnas.Select(c => c.Nombre)) + "\r\n")).ToArray();
        using var libro = new XLWorkbook();
        var datos = libro.AddWorksheet("Datos"); var ayuda = libro.AddWorksheet("Instrucciones");
        ayuda.Cell(1, 1).Value = "Máximo 10 MB y 20,000 renglones. Las celdas vacías no borran valores existentes. No use fórmulas. Los identificadores deben conservarse como texto.";
        for (var i = 0; i < columnas.Count; i++)
        {
            datos.Cell(1, i + 1).Value = columnas[i].Nombre;
            datos.Column(i + 1).Style.NumberFormat.Format = "@";
            ayuda.Cell(i + 3, 1).Value = columnas[i].Nombre; ayuda.Cell(i + 3, 2).Value = columnas[i].Ayuda;
            ayuda.Cell(i + 3, 3).Value = string.Join(", ", columnas[i].Alias);
        }
        datos.Columns().AdjustToContents(); ayuda.Columns().AdjustToContents(10, 90);
        using var salida = new MemoryStream(); libro.SaveAs(salida); return salida.ToArray();
    }
    private static BusinessRuleException Invalido(string mensaje) => new("CARGA_ARCHIVO_INVALIDO", mensaje);
}
