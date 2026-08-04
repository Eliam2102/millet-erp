namespace Millet.CuentasPorPagar.Domain.Cfdi;

/// <summary>
/// Puerto de almacenamiento de los XML/PDF de CFDIs (F1-PR1, ADR-0024).
/// Convención de path: <c>cxp/{año}/{mes}/cfdi/{uuid}.{ext}</c>.
/// Adapter real con Azure Blob en F10; stub filesystem para dev local.
/// </summary>
public interface ICfdiBlobStorage
{
    /// <summary>
    /// Almacena el XML del CFDI y devuelve la referencia/path. Idempotente
    /// por UUID: re-subir el mismo UUID con el mismo contenido devuelve la
    /// misma ref sin error.
    /// </summary>
    Task<string> GuardarXmlAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream contenido,
        CancellationToken cancellationToken);

    /// <summary>Almacena el PDF (opcional) y devuelve la ref. NULL si el contenido es null/vacío.</summary>
    Task<string?> GuardarPdfAsync(
        string uuid,
        DateTimeOffset fechaCfdi,
        Stream? contenido,
        CancellationToken cancellationToken);

    /// <summary>
    /// Abre el XML previamente guardado a partir de su referencia
    /// (<c>CfdiRecibido.XmlBlobRef</c>). Devuelve <c>null</c> si la ref
    /// no existe en el storage (blob purgado o path inválido). El caller
    /// es dueño del stream y debe disponerlo.
    /// </summary>
    Task<Stream?> LeerXmlAsync(string blobRef, CancellationToken cancellationToken);

    /// <summary>
    /// Abre el PDF previamente guardado a partir de su referencia
    /// (<c>CfdiRecibido.PdfBlobRef</c>). Mismas semánticas que
    /// <see cref="LeerXmlAsync"/>.
    /// </summary>
    Task<Stream?> LeerPdfAsync(string blobRef, CancellationToken cancellationToken);
}
