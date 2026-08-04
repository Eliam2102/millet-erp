namespace Millet.Compras.Application.Oc.Adjuntos.AdjuntarDocumento;

public sealed record AdjuntarDocumentoResponse(
    Guid AdjuntoId,
    string BlobUrl,
    DateTimeOffset FechaCarga);
