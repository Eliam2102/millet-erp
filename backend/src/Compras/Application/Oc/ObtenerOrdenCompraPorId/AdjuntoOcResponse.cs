namespace Millet.Compras.Application.Oc.ObtenerOrdenCompraPorId;

/// <summary>
/// DTO con los datos de un adjunto de la <see cref="Domain.Oc.OrdenCompra"/>
/// para el response de <c>GET /api/v1/compras/ordenes/{id}</c> (UF3-PR2).
/// Mirror directo de <see cref="Domain.Oc.AdjuntoOC"/>.
///
/// <para>El frontend consume este DTO en el <c>&lt;AdjuntosManager/&gt;</c>
/// del Tab "Adjuntos". No expone la URL del blob (F1-ADM-11 G1.2): preview
/// y descarga se sirven por el endpoint autenticado
/// <c>GET .../adjuntos/{adjuntoId}/contenido</c>.</para>
/// </summary>
public sealed record AdjuntoOcResponse(
    Guid Id,
    Guid TipoDocumentoId,
    string NombreArchivo,
    string ContentType,
    long TamanoBytes,
    DateTimeOffset FechaCarga,
    Guid UsuarioCargaId);
