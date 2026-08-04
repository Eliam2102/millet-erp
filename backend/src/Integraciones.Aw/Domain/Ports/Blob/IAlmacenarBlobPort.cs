namespace Millet.Integraciones.Aw.Domain.Ports.Blob;

/// <summary>
/// Puerto para almacenamiento de blobs del módulo Integraciones.Aw —
/// usado para persistir los PDF que A+W exporta (ofertas/pedidos) que el
/// <c>AwDocumentSyncWorker</c> descarga del drop service. En producción se
/// implementa contra Azure Blob Storage (ADR-0024); en dev/tests se usa
/// <c>LocalFilesystemBlobStub</c> que escribe a un directorio local.
///
/// <para>
/// Réplica del puerto homónimo de Compras/Almacén (mismo contrato): cada
/// módulo lo declara en su propio bounded context para no acoplar el
/// agregado a otro módulo. <see cref="SubirAsync"/> escribe bytes y
/// devuelve la URL/identificador; <see cref="ObtenerStreamAsync"/> lee
/// bytes para servir al cliente vía el endpoint proxy autenticado
/// <c>GET /cotizaciones/{id}/pdf</c> (la URL del blob NUNCA se expone al
/// Glass Agent).
/// </para>
/// </summary>
public interface IAlmacenarBlobPort
{
    /// <summary>
    /// Sube un blob al storage y devuelve su URL/identificador. El caller
    /// controla el GUID (típicamente el id de la entidad) para que la URL
    /// sea predecible y reproducible.
    /// </summary>
    Task<string> SubirAsync(
        Guid blobId,
        Stream contenido,
        string contentType,
        string nombreArchivoOriginal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Obtiene un stream legible del blob por su URL/identificador. Lanza
    /// si el blob no existe.
    /// </summary>
    Task<Stream> ObtenerStreamAsync(string blobUrl, CancellationToken cancellationToken);

    /// <summary>
    /// Elimina un blob por su URL/identificador. Idempotente. Puede ser
    /// no-op en backends que no soportan borrado granular.
    /// </summary>
    Task EliminarAsync(string blobUrl, CancellationToken cancellationToken);
}
