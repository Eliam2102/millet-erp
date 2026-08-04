namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;

/// <summary>
/// DTO con los datos de un adjunto de la <see cref="Domain.Oc.OrdenCompra"/>
/// para el response de <c>GET /api/v1/compras/ordenes/{id}</c> (UF3-PR2).
/// Mirror directo de <see cref="Domain.Oc.AdjuntoOC"/>.
///
/// <para>El frontend consume este DTO en el <c>&lt;AdjuntosManager/&gt;</c>
/// del Tab "Adjuntos". <see cref="BlobUrl"/> es la URL del blob storage
/// (Azure Blob en prod, filesystem stub en dev) — el frontend la usa
/// para preview (PDF embed / img thumbnail) y download.</para>
/// </summary>
public sealed record AdjuntoOcResponse(
    Guid Id,
    Guid TipoDocumentoId,
    string NombreArchivo,
    string BlobUrl,
    string ContentType,
    long TamanoBytes,
    DateTimeOffset FechaCarga,
    Guid UsuarioCargaId);
