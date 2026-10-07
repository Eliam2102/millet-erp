using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.Application.Adjuntos;

/// <summary>
/// Metadatos de un adjunto para el cliente. NUNCA incluye <c>BlobRef</c> ni URL de blob:
/// la descarga va por <c>contenido</c> o enlace temporal.
/// </summary>
public sealed record AdjuntoResponse(
    Guid Id,
    string TipoEntidad,
    Guid EntidadId,
    Guid TipoDocumentoId,
    string TipoDocumentoCodigo,
    string TipoDocumentoNombre,
    string NombreArchivo,
    string ContentType,
    long TamanoBytes,
    string HashSha256,
    DateOnly? VigenteHasta,
    EstadoAdjunto Estado,
    Guid SubidoPorId,
    DateTimeOffset SubidoEn,
    DateTimeOffset? BajaEn,
    Guid? BajaPorId,
    string? BajaMotivo)
{
    public static AdjuntoResponse De(Adjunto a, AdjuntoTipoDocumento tipo, DateOnly hoy) => new(
        a.Id, a.TipoEntidad, a.EntidadId, a.TipoDocumentoId, tipo.Codigo, tipo.Nombre,
        a.NombreArchivo, a.ContentType, a.TamanoBytes, a.HashSha256, a.VigenteHasta, a.EstadoEn(hoy),
        a.SubidoPorId, a.SubidoEn, a.BajaEn, a.BajaPorId, a.BajaMotivo);
}

public sealed record AdjuntoTipoDocumentoResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    int Orden,
    bool Obligatorio,
    int? VigenciaMeses,
    bool SoloPersonaMoral);
