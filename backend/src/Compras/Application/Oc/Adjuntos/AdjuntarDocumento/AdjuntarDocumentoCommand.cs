using MediatR;

namespace Millet.Compras.Application.Oc.Adjuntos.AdjuntarDocumento;

/// <summary>
/// Adjuntar un documento a una OC. El blob ya se subió al storage por
/// el endpoint (multipart/form-data); este comando solo persiste la
/// metadata. <see cref="UsuarioCargaId"/> y <see cref="FechaCarga"/>
/// los resuelve el handler desde el JWT y <c>IClock</c>.
/// </summary>
public sealed record AdjuntarDocumentoCommand(
    Guid OrdenCompraId,
    Guid TipoDocumentoId,
    string NombreArchivo,
    string BlobUrl,
    string ContentType,
    long TamañoBytes) : IRequest<AdjuntarDocumentoResponse>;
