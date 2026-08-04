namespace Millet.Integraciones.Aw.Application.Ports;

/// <summary>
/// Puerto out-going que abstrae la lectura de los PDF que A+W exporta,
/// expuestos por el drop service on-prem en <c>GET /documents</c> (lista)
/// y <c>GET /documents/{filename}</c> (descarga). Lo consume el
/// <c>AwDocumentSyncWorker</c> para subir cada PDF a Blob Storage y
/// publicar la URL al Glass Agent.
///
/// <para>
/// La semántica de <see cref="ListSinceAsync"/> es cursor-based
/// (<c>since</c> estricto por <c>modified_at</c> + <c>limit</c>); el caller
/// pagina pasando <see cref="AwDocumentsPage.NextSince"/>. Implementaciones
/// de prueba pueden devolver páginas sin servidor real.
/// </para>
/// </summary>
public interface IAwDocumentsReader
{
    /// <summary>
    /// Lista documentos con <c>ModifiedAt &gt; since</c>. Si
    /// <paramref name="since"/> es <c>null</c>, devuelve desde el más
    /// antiguo disponible (limitado por la retención de la carpeta de
    /// A+W).
    /// </summary>
    /// <exception cref="AwDocumentsException">
    /// Falla HTTP (auth, red, 5xx, body inválido).
    /// </exception>
    Task<AwDocumentsPage> ListSinceAsync(
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Descarga el contenido de un PDF por su <paramref name="filename"/>.
    /// El stream devuelto es propiedad del caller (debe disponerlo). Útil
    /// para subir el contenido directo a Blob Storage sin bufferizar en
    /// disco.
    /// </summary>
    /// <exception cref="AwDocumentsException">
    /// Falla HTTP. <c>Kind="not_found"</c> (404), <c>IsTransient=false</c>
    /// si el archivo ya no existe en el drop service.
    /// </exception>
    Task<Stream> DownloadAsync(
        string filename,
        CancellationToken cancellationToken);

    /// <summary>
    /// Marca un PDF como TRATADO: el drop service lo mueve a su subcarpeta
    /// <c>archive\</c> para que la carpeta caliente se autolimpie. Se llama
    /// SOLO después de subir el PDF a blob y persistir. Idempotente — si el
    /// archivo ya no está, no es error.
    /// </summary>
    /// <exception cref="AwDocumentsException">
    /// Falla HTTP transitoria (red, 5xx). El caller la trata como
    /// best-effort: si falla, el PDF se re-tratará el próximo ciclo (la
    /// idempotencia del ERP evita re-subir).
    /// </exception>
    Task ArchivarAsync(
        string filename,
        CancellationToken cancellationToken);
}

/// <summary>
/// Página de documentos devuelta por <see cref="IAwDocumentsReader"/>.
/// </summary>
public sealed record AwDocumentsPage(
    IReadOnlyList<AwDocumentItem> Documents,
    DateTimeOffset? NextSince);

/// <summary>
/// Una entrada del endpoint <c>GET /documents</c>: un PDF que A+W exportó.
/// </summary>
/// <param name="Filename">Nombre del PDF (ej. <c>oferta_4614.pdf</c>).</param>
/// <param name="DocType"><c>"oferta"</c> o <c>"pedido"</c>, derivado del prefijo del nombre.</param>
/// <param name="AwDocId">Número de documento A+W (= <c>aw_doc_id</c> de la entidad correlacionada).</param>
/// <param name="SizeBytes">Tamaño del archivo en bytes.</param>
/// <param name="ModifiedAt">Timestamp de última escritura del archivo (cursor de paginación).</param>
public sealed record AwDocumentItem(
    string Filename,
    string DocType,
    long AwDocId,
    long SizeBytes,
    DateTimeOffset ModifiedAt);
