using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.Application.Adjuntos;

/// <summary>
/// Política de formatos, tipos MIME, tamaño máximo y firmas (magic bytes) permitidas
/// para adjuntos de documentos en el ERP.
///
/// <para>
/// CONFIGURACIÓN DE PRUEBA (F1-ADM-11) — formatos, tamaño y retención pendientes
/// de confirmar con Millet. Los límites por defecto coinciden con la lista blanca del
/// frontend (AdjuntosManager.tsx) y se configuran bajo la sección <c>Adjuntos:Politica</c>.
/// </para>
/// </summary>
public sealed class AdjuntosPoliticaOptions
{
    public const string SectionName = "Adjuntos:Politica";

    /// <summary>
    /// Metadata de origen para documentar en configuración que los valores son de prueba.
    /// </summary>
    public string? Origen { get; set; }

    /// <summary>
    /// Tamaño máximo en bytes (por defecto 20 MB = 20 * 1024 * 1024).
    /// </summary>
    public long MaxBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Extensiones de archivo permitidas (con o sin punto inicial, insensible a mayúsculas).
    /// </summary>
    public List<string> Extensiones { get; set; } =
    [
        ".pdf",
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif",
        ".xlsx",
        ".xls",
        ".docx",
        ".doc",
        ".txt"
    ];

    /// <summary>
    /// Tipos MIME permitidos (insensible a mayúsculas).
    /// </summary>
    public List<string> ContentTypes { get; set; } =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/msword",
        "text/plain"
    ];

    /// <summary>
    /// Overrides por tipo de entidad dueña (clave en snake_case, p. ej. <c>proveedor</c>).
    /// Lo no definido en un override se hereda del default global.
    /// </summary>
    public Dictionary<string, AdjuntosPoliticaEntidadOptions> PorTipoEntidad { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Devuelve la política efectiva para un tipo de entidad (override si existe, si no esta misma).
    /// </summary>
    public AdjuntosPoliticaOptions ParaEntidad(string tipoEntidad)
    {
        if (!PorTipoEntidad.TryGetValue(tipoEntidad, out var o))
            return this;

        return new AdjuntosPoliticaOptions
        {
            MaxBytes = o.MaxBytes ?? MaxBytes,
            Extensiones = o.Extensiones is { Count: > 0 } ? o.Extensiones : Extensiones,
            ContentTypes = o.ContentTypes is { Count: > 0 } ? o.ContentTypes : ContentTypes
        };
    }

    private static readonly AdjuntosPoliticaOptions DefaultOptions = new();

    /// <summary>
    /// Valida un archivo contra la política de adjuntos por defecto.
    /// </summary>
    public static void Validar(
        string nombreArchivo,
        string contentType,
        long longitud,
        ReadOnlySpan<byte> cabecera)
    {
        DefaultOptions.ValidarInstancia(nombreArchivo, contentType, longitud, cabecera);
    }

    /// <summary>
    /// Valida un archivo contra esta instancia de opciones de política de adjuntos.
    /// Lanza <see cref="BusinessRuleException"/> con código <c>ADJUNTO_FORMATO_NO_PERMITIDO</c>
    /// o <c>ADJUNTO_TAMANO_EXCEDIDO</c> si alguna regla no se cumple.
    /// </summary>
    public void ValidarInstancia(
        string nombreArchivo,
        string contentType,
        long longitud,
        ReadOnlySpan<byte> cabecera)
    {
        // 1. Tamaño máximo
        if (longitud > MaxBytes)
        {
            var maxMb = MaxBytes / (1024 * 1024);
            throw new BusinessRuleException(
                "ADJUNTO_TAMANO_EXCEDIDO",
                $"El archivo excede el tamaño máximo permitido de {maxMb} MB.");
        }

        // 2. Extensión permitida (se limpia cualquier ruta en el nombre)
        var nombreLimpio = Path.GetFileName(nombreArchivo);
        var extension = Path.GetExtension(nombreLimpio);

        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new BusinessRuleException(
                "ADJUNTO_FORMATO_NO_PERMITIDO",
                "El archivo debe tener una extensión válida.");
        }

        var extensionNormalizada = extension.StartsWith('.') ? extension : $".{extension}";
        var extensionPermitida = Extensiones.Any(e =>
        {
            var normalizada = e.StartsWith('.') ? e : $".{e}";
            return string.Equals(normalizada, extensionNormalizada, StringComparison.OrdinalIgnoreCase);
        });

        if (!extensionPermitida)
        {
            throw new BusinessRuleException(
                "ADJUNTO_FORMATO_NO_PERMITIDO",
                $"El formato '{extensionNormalizada}' no está permitido.");
        }

        // 3. Content-Type permitido
        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new BusinessRuleException(
                "ADJUNTO_FORMATO_NO_PERMITIDO",
                "El tipo de contenido (Content-Type) es requerido.");
        }

        var contentTypePermitido = ContentTypes.Any(ct =>
            string.Equals(ct.Trim(), contentType.Trim(), StringComparison.OrdinalIgnoreCase));

        if (!contentTypePermitido)
        {
            throw new BusinessRuleException(
                "ADJUNTO_FORMATO_NO_PERMITIDO",
                $"El tipo de contenido '{contentType}' no está permitido.");
        }

        // 4. Firmas binarias (magic bytes) para formatos estructurados
        // ponytail: techo = sin antivirus ni firma para Office/txt; añadir si Millet lo exige.
        var esPdf = string.Equals(extensionNormalizada, ".pdf", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(contentType.Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase);

        if (esPdf)
        {
            // Firma PDF: %PDF (0x25, 0x50, 0x44, 0x46)
            if (cabecera.Length < 4
                || cabecera[0] != 0x25
                || cabecera[1] != 0x50
                || cabecera[2] != 0x44
                || cabecera[3] != 0x46)
            {
                throw new BusinessRuleException(
                    "ADJUNTO_FORMATO_NO_PERMITIDO",
                    "El archivo no corresponde a un documento PDF válido.");
            }
        }

        var esXml = string.Equals(extensionNormalizada, ".xml", StringComparison.OrdinalIgnoreCase)
                    || contentType.Trim().Equals("application/xml", StringComparison.OrdinalIgnoreCase)
                    || contentType.Trim().Equals("text/xml", StringComparison.OrdinalIgnoreCase);

        if (esXml)
        {
            // Solo se verifica que arranque como XML ('<' tras BOM UTF-8 opcional); NO se parsea (evita XXE).
            var inicio = cabecera.Length >= 3 && cabecera[0] == 0xEF && cabecera[1] == 0xBB && cabecera[2] == 0xBF ? 3 : 0;
            if (cabecera.Length <= inicio || cabecera[inicio] != (byte)'<')
            {
                throw new BusinessRuleException(
                    "ADJUNTO_FORMATO_NO_PERMITIDO",
                    "El archivo no corresponde a un documento XML válido.");
            }
        }

        var esPng = string.Equals(extensionNormalizada, ".png", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(contentType.Trim(), "image/png", StringComparison.OrdinalIgnoreCase);

        if (esPng)
        {
            // Firma PNG: 89 50 4E 47
            if (cabecera.Length < 4
                || cabecera[0] != 0x89
                || cabecera[1] != 0x50
                || cabecera[2] != 0x4E
                || cabecera[3] != 0x47)
            {
                throw new BusinessRuleException(
                    "ADJUNTO_FORMATO_NO_PERMITIDO",
                    "El archivo no corresponde a una imagen PNG válida.");
            }
        }

        var esJpg = string.Equals(extensionNormalizada, ".jpg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extensionNormalizada, ".jpeg", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(contentType.Trim(), "image/jpeg", StringComparison.OrdinalIgnoreCase);

        if (esJpg)
        {
            // Firma JPEG: FF D8 FF
            if (cabecera.Length < 3
                || cabecera[0] != 0xFF
                || cabecera[1] != 0xD8
                || cabecera[2] != 0xFF)
            {
                throw new BusinessRuleException(
                    "ADJUNTO_FORMATO_NO_PERMITIDO",
                    "El archivo no corresponde a una imagen JPEG válida.");
            }
        }
    }
}

/// <summary>
/// Override de política para un tipo de entidad. Listas vacías / <c>MaxBytes</c> nulo = hereda el default global
/// (sin valores por defecto propios para que el binder de configuración no mezcle listas).
/// </summary>
public sealed class AdjuntosPoliticaEntidadOptions
{
    public long? MaxBytes { get; set; }
    public List<string> Extensiones { get; set; } = [];
    public List<string> ContentTypes { get; set; } = [];
}
