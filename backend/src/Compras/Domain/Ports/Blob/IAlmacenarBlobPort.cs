namespace Millet.Compras.Domain.Ports.Blob;

/// <summary>
/// Puerto para almacenamiento de blobs (adjuntos de OC, PDFs generados,
/// etc.). En producción se implementa contra Azure Blob Storage
/// (ADR-0024); en desarrollo y tests se usa
/// <c>LocalFilesystemBlobStub</c> que escribe a un directorio local.
///
/// La carga separa <see cref="SubirAsync"/> (escribe bytes y devuelve
/// URL) y <see cref="ObtenerStreamAsync"/> (lee bytes para servir al
/// cliente vía endpoint REST). La eliminación es opcional —
/// <see cref="EliminarAsync"/> puede ser no-op si el storage no soporta
/// borrado individual; los blobs huérfanos se limpian con un proceso
/// periódico aparte (post-MVP).
///
/// F10-PR3: <c>AzureBlobAlmacenarBlobPort</c> es el wireup real contra
/// Azure Blob Storage. Se selecciona cuando
/// <c>Compras:Oc:BlobStorage:ConnectionString</c> está configurado;
/// sin connection string, se mantiene <c>LocalFilesystemBlobStub</c>
/// para dev local.
/// </summary>
public interface IAlmacenarBlobPort
{
    /// <summary>
    /// Sube un blob al storage y devuelve su URL/identificador. El
    /// caller controla el GUID (suele ser el id del adjunto) para que
    /// la URL sea predecible y reproducible.
    /// </summary>
    /// <param name="blobId">Identificador único; típicamente el id del
    ///   adjunto en la BD. El nombre final en el storage usa este id
    ///   + extensión inferida del content type o nombre original.</param>
    /// <param name="contenido">Stream con los bytes del archivo.</param>
    /// <param name="contentType">MIME type (application/pdf,
    ///   image/jpeg, etc.). Persiste en la metadata del blob.</param>
    /// <param name="nombreArchivoOriginal">Nombre con extensión que el
    ///   cliente envió. Se usa para extraer la extensión cuando el
    ///   content type es genérico.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>URL/identificador del blob almacenado.</returns>
    Task<string> SubirAsync(
        Guid blobId,
        Stream contenido,
        string contentType,
        string nombreArchivoOriginal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un stream legible del blob por su URL/identificador.
    /// Lanza si el blob no existe.
    /// </summary>
    Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Elimina un blob por su URL/identificador. Idempotente:
    /// re-eliminar no lanza. Puede ser no-op en backends que no
    /// soportan borrado granular (los blobs huérfanos se limpian
    /// aparte).
    /// </summary>
    Task EliminarAsync(string blobUrl, CancellationToken cancellationToken);
}
