namespace Millet.Compras.Domain.Ports.Pdf;

/// <summary>
/// Puerto para generar el PDF institucional de una OC (F6-PR1).
/// Recibe el agregado y devuelve los bytes del PDF + content type.
///
/// <para>
/// Implementación stub <c>LocalPdfOrdenCompraStub</c> en F6-PR1
/// (texto plano con folio + total). Reemplazado por
/// <c>QuestPdfOrdenCompraImpl</c> en F6-PR3 con layout institucional
/// real.
/// </para>
///
/// <para>
/// La agrupación cosmética de líneas por artículo (§3.bis.4 — mismas
/// artículos de RQs distintas se muestran agregados en el PDF al
/// proveedor) la hace el servicio caller antes de invocar el port.
/// </para>
/// </summary>
public interface IGenerarPdfOrdenCompraPort
{
    /// <summary>
    /// Genera el contenido del PDF de la OC. El caller maneja
    /// persistencia y subida al blob storage.
    /// </summary>
    Task<PdfOrdenCompraGenerado> GenerarAsync(
        Millet.Compras.Domain.Oc.OrdenCompra oc,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultado de <see cref="IGenerarPdfOrdenCompraPort.GenerarAsync"/>:
/// bytes del PDF + metadata mínima para subir al blob storage.
/// </summary>
public sealed record PdfOrdenCompraGenerado(
    byte[] Contenido,
    string ContentType,
    string NombreSugerido);
