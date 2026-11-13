namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

/// <summary>
/// Fábrica de parsers de extracto bancario con perfil por banco (T9.2).
/// Resuelve el parser correspondiente (BBVA 10 col / 6 col, Santander / Getnet, o genérico).
/// Permite resolución por perfil explícito o detección inteligente por cabecera y estructura.
/// </summary>
public sealed class ExtractoParserFactory
{
    private readonly Dictionary<string, IExtractoParser> _parsers = new(StringComparer.OrdinalIgnoreCase);

    public ExtractoParserFactory(IEnumerable<IExtractoParser>? parsers = null)
    {
        if (parsers != null)
        {
            foreach (var p in parsers)
            {
                _parsers[p.PerfilBanco] = p;
            }
        }

        // Registrar BBVA si no existe
        if (!_parsers.TryGetValue("BBVA", out var bbva))
        {
            bbva = new BbvaExtractoParser();
            _parsers["BBVA"] = bbva;
        }

        // Registrar Santander si no existe
        if (!_parsers.TryGetValue("SANTANDER", out var santander))
        {
            santander = new SantanderExtractoParser();
            _parsers["SANTANDER"] = santander;
        }

        // MIFEL: perfil listo, sin layout hasta recibir el archivo real (M-102).
        _parsers.TryAdd("MIFEL", new MifelExtractoParser());

        // Alias canónicos
        _parsers.TryAdd("GENERICO", bbva);
        _parsers.TryAdd("CSV", bbva);
        _parsers.TryAdd("GETNET", santander);
    }

    public IExtractoParser ObtenerParser(string? perfilBanco) => ObtenerParser(perfilBanco, null);

    public IExtractoParser ObtenerParser(string? perfilBanco, string? contenidoArchivo)
    {
        string? perfil = perfilBanco?.Trim();

        // 1. Si no hay contenido para detectar, resolver por nombre de perfil directamente
        if (string.IsNullOrWhiteSpace(contenidoArchivo))
        {
            if (!string.IsNullOrWhiteSpace(perfil) && _parsers.TryGetValue(perfil, out var parser))
            {
                return parser;
            }
            return _parsers["BBVA"];
        }

        // 2. Si el perfil es explícitamente Santander o Getnet, respetarlo
        if (!string.IsNullOrWhiteSpace(perfil) && (perfil.Equals("SANTANDER", StringComparison.OrdinalIgnoreCase) || perfil.Equals("GETNET", StringComparison.OrdinalIgnoreCase)))
        {
            return _parsers["SANTANDER"];
        }

        // 3. Detección inteligente por estructura y cabecera
        var parserDetectado = DetectarParser(contenidoArchivo);

        // Si el usuario especificó un perfil distinto al default "BBVA" (y no es alias genérico/auto), respetarlo a menos que sea una detección clara
        if (!string.IsNullOrWhiteSpace(perfil) &&
            !perfil.Equals("BBVA", StringComparison.OrdinalIgnoreCase) &&
            !perfil.Equals("GENERICO", StringComparison.OrdinalIgnoreCase) &&
            !perfil.Equals("CSV", StringComparison.OrdinalIgnoreCase) &&
            !perfil.Equals("AUTO", StringComparison.OrdinalIgnoreCase) &&
            _parsers.TryGetValue(perfil, out var parserEspecifico))
        {
            return parserEspecifico;
        }

        return parserDetectado;
    }

    public IExtractoParser DetectarParser(string? contenidoArchivo)
    {
        if (string.IsNullOrWhiteSpace(contenidoArchivo))
        {
            return _parsers["BBVA"];
        }

        // Tomar las primeras 15 líneas para analizar la cabecera o estructura
        var primerasLineas = contenidoArchivo
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Take(15)
            .ToList();

        string encabezadosMuestra = string.Join("\n", primerasLineas).ToUpperInvariant();

        // Cabecera unificada Cargo/Abono de Getnet/Santander. No se busca la palabra «SANTANDER»:
        // el auxiliar BBVA la trae en la referencia de los CIE de clientes con cuenta Santander.
        if (encabezadosMuestra.Contains("CARGO/ABONO") ||
            encabezadosMuestra.Contains("CARGO / ABONO") ||
            encabezadosMuestra.Contains("CARGO_ABONO"))
        {
            return _parsers["SANTANDER"];
        }

        // Si alguna línea contiene importes con signos explícitos (+/-) junto con fechas compactas DDMMAAAA
        foreach (var linea in primerasLineas)
        {
            if (linea.Contains(",+") || linea.Contains(",-") || linea.Contains(";+") || linea.Contains(";-"))
            {
                return _parsers["SANTANDER"];
            }
        }

        return _parsers["BBVA"];
    }
}
