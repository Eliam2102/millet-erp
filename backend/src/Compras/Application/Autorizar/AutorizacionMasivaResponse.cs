namespace Millet.Compras.Application.Autorizar;

/// <summary>
/// Resultado de la autorización masiva (S2). Un elemento por id solicitado,
/// en el mismo orden. Un fallo individual no aborta el lote.
/// </summary>
public sealed record AutorizacionMasivaResponse(IReadOnlyList<AutorizacionMasivaItemResultado> Resultados);

/// <summary>
/// Resultado de un id. Si <see cref="Ok"/> es <c>false</c>, <see cref="ErrorCodigo"/>
/// es el código estable de la excepción de dominio y <see cref="Mensaje"/> su texto.
/// </summary>
public sealed record AutorizacionMasivaItemResultado(
    Guid Id,
    bool Ok,
    string? ErrorCodigo,
    string? Mensaje);
