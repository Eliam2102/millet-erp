using System.Globalization;
using System.IO.Compression;
using System.Text;
using Millet.Facturacion.Domain.ConciliacionSat;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.ConciliacionSat;

/// <summary>
/// Lee el archivo de metadatos de CFDI que entrega el SAT (descarga masiva o
/// portal): texto con encabezado y campos separados por «~»
/// (<c>Uuid~RfcEmisor~NombreEmisor~RfcReceptor~NombreReceptor~RfcPac~FechaEmision~FechaCertificacionSat~Monto~EfectoComprobante~Estatus~FechaCancelacion</c>),
/// suelto o dentro del ZIP del paquete. Las columnas se ubican por nombre, no por
/// posición; también acepta «|» como separador. Estatus 1 = vigente, 0 = cancelado.
/// </summary>
public static class LectorMetadataSat
{
    private static readonly string[] Requeridas = ["Uuid", "RfcEmisor", "RfcReceptor", "FechaEmision", "Monto", "EfectoComprobante", "Estatus"];
    private static readonly string[] FormatosFecha = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd", "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy"];
    private const int MaximoErroresMostrados = 5;

    public static IReadOnlyList<CfdiSatMetadata> Leer(byte[] contenido)
    {
        var texto = Texto(contenido);
        var lineas = texto.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var inicio = lineas.FindIndex(l => !string.IsNullOrWhiteSpace(l));
        if (inicio < 0)
            throw new BusinessRuleException("CONCILIACION_SAT_ARCHIVO_VACIO", "El archivo del SAT no trae renglones.");

        var encabezado = lineas[inicio];
        var separador = encabezado.Contains('~') ? '~' : encabezado.Contains('|') ? '|' : '\0';
        if (separador == '\0')
            throw new BusinessRuleException("CONCILIACION_SAT_FORMATO",
                "No reconozco el archivo: se espera el de metadatos del SAT, con encabezado y campos separados por «~».");
        var columnas = encabezado.Split(separador).Select((c, i) => (Nombre: c.Trim(), i))
            .GroupBy(c => c.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        var faltan = Requeridas.Where(r => !columnas.ContainsKey(r)).ToList();
        if (faltan.Count > 0)
            throw new BusinessRuleException("CONCILIACION_SAT_COLUMNAS",
                $"Al archivo del SAT le faltan las columnas: {string.Join(", ", faltan)}.");

        var cfdi = new List<CfdiSatMetadata>();
        var errores = new List<string>();
        for (var n = inicio + 1; n < lineas.Count; n++)
        {
            var linea = lineas[n];
            if (string.IsNullOrWhiteSpace(linea)) continue;
            var campos = linea.Split(separador);
            string Campo(string nombre) =>
                columnas.TryGetValue(nombre, out var i) && i < campos.Length ? campos[i].Trim() : string.Empty;
            try
            {
                var uuid = Campo("Uuid");
                if (!Guid.TryParse(uuid, out _))
                    throw new FormatException($"UUID inválido «{uuid}»");
                cfdi.Add(new CfdiSatMetadata(
                    ComparadorConciliacionSat.Normalizar(uuid),
                    Requerido(Campo("RfcEmisor"), "RfcEmisor").ToUpperInvariant(),
                    Requerido(Campo("RfcReceptor"), "RfcReceptor").ToUpperInvariant(),
                    Opcional(Campo("NombreReceptor")),
                    Fecha(Campo("FechaEmision"), "FechaEmision"),
                    Importe(Campo("Monto")),
                    Efecto(Campo("EfectoComprobante")),
                    Cancelado(Campo("Estatus")),
                    Opcional(Campo("FechaCancelacion")) is { } fc ? Fecha(fc, "FechaCancelacion") : null));
            }
            catch (FormatException ex)
            {
                errores.Add($"renglón {n + 1}: {ex.Message}");
            }
        }

        if (errores.Count > 0)
            throw new BusinessRuleException("CONCILIACION_SAT_RENGLONES",
                $"{errores.Count} renglón(es) del archivo del SAT no se pudieron leer: " +
                string.Join("; ", errores.Take(MaximoErroresMostrados)) + (errores.Count > MaximoErroresMostrados ? "; …" : "."));
        if (cfdi.Count == 0)
            throw new BusinessRuleException("CONCILIACION_SAT_ARCHIVO_VACIO", "El archivo del SAT no trae CFDI.");
        return cfdi;
    }

    /// <summary>Texto del archivo; si es ZIP (paquete de la descarga masiva) toma el primer .txt.</summary>
    private static string Texto(byte[] contenido)
    {
        if (contenido.Length >= 4 && contenido[0] == 'P' && contenido[1] == 'K')
        {
            using var zip = new ZipArchive(new MemoryStream(contenido), ZipArchiveMode.Read);
            var txt = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                ?? throw new BusinessRuleException("CONCILIACION_SAT_ZIP_SIN_TXT", "El ZIP no trae el archivo .txt de metadatos del SAT.");
            using var s = txt.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            return Decodificar(m.ToArray());
        }
        return Decodificar(contenido);
    }

    /// <summary>UTF-8 (con o sin BOM); si no es UTF-8 válido, Latin-1 (descargas viejas del portal).</summary>
    private static string Decodificar(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string Requerido(string v, string campo) =>
        string.IsNullOrWhiteSpace(v) ? throw new FormatException($"falta {campo}") : v;

    private static string? Opcional(string v) => string.IsNullOrWhiteSpace(v) ? null : v;

    private static DateTime Fecha(string v, string campo) =>
        DateTime.TryParseExact(v, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
            ? f
            : throw new FormatException($"{campo} inválida «{v}»");

    private static decimal Importe(string v)
    {
        var limpio = v.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out var m)
            ? m
            : throw new FormatException($"Monto inválido «{v}»");
    }

    private static string Efecto(string v)
    {
        var e = v.Trim().ToUpperInvariant();
        return e switch
        {
            "I" or "INGRESO" => "I",
            "E" or "EGRESO" => "E",
            "T" or "TRASLADO" => "T",
            "P" or "PAGO" => "P",
            "N" or "NOMINA" or "NÓMINA" => "N",
            _ => throw new FormatException($"EfectoComprobante inválido «{v}»"),
        };
    }

    private static bool Cancelado(string v) => v.Trim().ToUpperInvariant() switch
    {
        "1" or "VIGENTE" => false,
        "0" or "CANCELADO" => true,
        _ => throw new FormatException($"Estatus inválido «{v}»"),
    };
}
