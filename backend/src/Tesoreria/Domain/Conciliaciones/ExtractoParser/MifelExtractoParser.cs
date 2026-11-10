namespace Millet.Tesoreria.Domain.Conciliaciones.ExtractoParser;

/// <summary>
/// Perfil MIFEL: queda registrado para las cuentas Mifel, pero sin layout hasta recibir un estado de
/// cuenta real del banco (M-102). No se adivinan columnas: todo archivo se rechaza con un error claro.
/// </summary>
public sealed class MifelExtractoParser : IExtractoParser
{
    public string PerfilBanco => "MIFEL";

    public ResultadoParseExtracto Parse(Stream stream) => Parse(string.Empty);

    public ResultadoParseExtracto Parse(string contenido) =>
        throw ExtractoParseo.FormatoNoReconocido(PerfilBanco,
            "aún no hay layout de Mifel; falta el archivo real del banco (M-102).");
}
