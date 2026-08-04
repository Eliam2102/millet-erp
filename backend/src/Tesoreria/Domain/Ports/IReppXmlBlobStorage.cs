namespace Millet.Tesoreria.Domain.Ports;

/// <summary>
/// Puerto de almacenamiento del XML del REPP recibido del proveedor
/// (TES-PR8, ADR-0024 — réplica del patrón <c>ICfdiBlobStorage</c> de
/// CxP). Convención de path:
/// <c>tesoreria/{año}/{mes}/repp/{uuid}.xml</c>. Adapter real con Azure
/// Blob cuando hay connection string; stub filesystem para dev local.
/// </summary>
public interface IReppXmlBlobStorage
{
    /// <summary>
    /// Almacena el XML del complemento y devuelve la referencia/path.
    /// Idempotente por UUID: re-subir el mismo UUID devuelve la misma
    /// ref sin error.
    /// </summary>
    Task<string> GuardarXmlAsync(
        string uuid,
        DateOnly fechaComplemento,
        Stream contenido,
        CancellationToken cancellationToken);

    /// <summary>
    /// Abre el XML previamente guardado a partir de su referencia
    /// (<c>ReppProveedorRecibido.XmlBlobRef</c>). <c>null</c> si la ref
    /// no existe. El caller es dueño del stream.
    /// </summary>
    Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken);
}
