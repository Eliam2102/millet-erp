namespace Millet.Almacen.Domain.Ports.Blob;

/// <summary>
/// Puerto para almacenamiento de blobs del módulo Almacén (F2-PR4):
/// packing lists de recepciones (Variante B), evidencias de
/// devoluciones, comprobantes de salidas, fotos de inventario físico.
///
/// <para>
/// Mismo shape que <c>Compras.Domain.Ports.Blob.IAlmacenarBlobPort</c>
/// pero en el namespace de Almacén para preservar el bounded context.
/// La instancia <c>BlobServiceClient</c> subyacente sí es compartida
/// (cableada en <c>Program.cs</c>) — lo per-módulo es el container y
/// las políticas de acceso/retención.
/// </para>
///
/// <para>
/// En producción se implementa contra Azure Blob Storage
/// (<see cref="Infrastructure.Blob.AzureBlobAlmacenarBlobPort"/>); en
/// desarrollo y tests se usa
/// <see cref="Infrastructure.Stubs.LocalFilesystemAlmacenBlobStub"/>
/// que escribe a un directorio local. La selección se hace en
/// <c>Program.cs</c> según si <c>Almacen:BlobStorage:ConnectionString</c>
/// está configurado.
/// </para>
/// </summary>
public interface IAlmacenarBlobPort
{
    /// <summary>
    /// Sube un blob al storage y devuelve su URL/identificador. El
    /// caller controla el GUID (suele ser el id del adjunto/movimiento)
    /// para que la URL sea predecible y reproducible.
    /// </summary>
    /// <param name="blobId">Identificador único; típicamente el id del
    ///   adjunto o de la entidad propietaria. El nombre final usa este
    ///   id + extensión inferida del content type o nombre original.</param>
    /// <param name="contenido">Stream con los bytes del archivo.</param>
    /// <param name="contentType">MIME type (application/pdf,
    ///   image/jpeg, etc.). Persiste en la metadata del blob.</param>
    /// <param name="nombreArchivoOriginal">Nombre con extensión que el
    ///   cliente envió. Se usa para extraer la extensión cuando el
    ///   content type es genérico.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>URL/identificador del blob almacenado. Apto para
    ///   guardarse como <c>packing_list_blob_ref</c> de
    ///   <c>MovimientoInventario</c>.</returns>
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
    /// Obtiene el blob junto con su metadata (content type + nombre
    /// original) en una sola llamada. Diseñado para endpoints HTTP de
    /// descarga/preview que necesitan setear <c>Content-Type</c> y
    /// <c>Content-Disposition</c> correctamente.
    ///
    /// <para>Si el backend subyacente no preserva la metadata (caso
    /// raro), implementar inferiendo el <c>ContentType</c> desde la
    /// extensión del <c>blobUrl</c>.</para>
    /// </summary>
    Task<BlobDescriptor> ObtenerDescriptorAsync(string blobUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Elimina un blob por su URL/identificador. Idempotente:
    /// re-eliminar no lanza. Puede ser no-op en backends que no
    /// soportan borrado granular (los blobs huérfanos se limpian
    /// con un proceso periódico aparte, post-MVP).
    /// </summary>
    Task EliminarAsync(string blobUrl, CancellationToken cancellationToken);
}

/// <summary>
/// Wrapper del blob con su metadata. Devuelto por
/// <see cref="IAlmacenarBlobPort.ObtenerDescriptorAsync"/>.
/// </summary>
/// <param name="Contenido">Stream legible. El caller es responsable de
///   liberarlo (using o dispose).</param>
/// <param name="ContentType">MIME type real del blob (preservado al
///   subir) — p.ej. <c>application/pdf</c>, <c>image/jpeg</c>. Si no se
///   puede resolver, <c>application/octet-stream</c>.</param>
/// <param name="NombreArchivo">Nombre original con extensión, útil
///   para el header <c>Content-Disposition</c>. <c>null</c> si el
///   backend no lo preserva.</param>
public sealed record BlobDescriptor(
    Stream Contenido,
    string ContentType,
    string? NombreArchivo);
