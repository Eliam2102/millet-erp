namespace Millet.Compras.Application.Oc.Adjuntos.AdjuntarDocumento;

public sealed record AdjuntarDocumentoResponse(
    Guid AdjuntoId,
    DateTimeOffset FechaCarga);
