namespace Millet.Compartido.Application.Ports;

/// <summary>
/// M-373: marca un tipo de documento como condicional (se pide sólo cuando aplica: puede registrarse
/// «No aplica» con motivo). Compartido es dueño de <c>compartido.adjunto_tipos_documento</c>; otros
/// módulos (Comercio exterior) lo cambian por este puerto y no escriben la tabla.
/// </summary>
public interface ITipoDocumentoCondicionalPort
{
    Task CambiarCondicionalAsync(string tipoEntidad, Guid tipoDocumentoId, bool condicional, CancellationToken cancellationToken);
}
