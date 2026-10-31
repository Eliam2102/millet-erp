namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

/// <summary>
/// Fábrica de parsers de extracto bancario con perfil por banco (T9.2).
/// Resuelve el parser correspondiente (BBVA primero, genérico/CSV por defecto).
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

        // Registrar BBVA por defecto
        if (!_parsers.ContainsKey("BBVA"))
        {
            var bbva = new BbvaExtractoParser();
            _parsers["BBVA"] = bbva;
            _parsers["GENERICO"] = bbva;
            _parsers["CSV"] = bbva;
        }
    }

    public IExtractoParser ObtenerParser(string? perfilBanco)
    {
        string perfil = string.IsNullOrWhiteSpace(perfilBanco) ? "BBVA" : perfilBanco.Trim();
        if (_parsers.TryGetValue(perfil, out var parser))
        {
            return parser;
        }

        return _parsers["BBVA"];
    }
}
