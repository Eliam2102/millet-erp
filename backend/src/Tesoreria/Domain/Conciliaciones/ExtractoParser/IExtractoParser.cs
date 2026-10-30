namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

/// <summary>
/// Puerto/interfaz para los parsers de extracto bancario con perfil por banco (T9.2).
/// </summary>
public interface IExtractoParser
{
    string PerfilBanco { get; }
    ResultadoParseExtracto Parse(string contenido);
    ResultadoParseExtracto Parse(Stream stream);
}
