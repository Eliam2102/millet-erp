namespace Millet.SharedKernel.Application.Adjuntos;

/// <summary>Enlace temporal emitido: token opaco y su expiración (UTC).</summary>
public sealed record AdjuntoEnlaceEmitido(string Token, DateTimeOffset ExpiraEn);

/// <summary>Contenido de un token válido.</summary>
public sealed record AdjuntoEnlaceClaims(Guid AdjuntoId, Guid UsuarioId);

/// <summary>
/// Token de descarga de vida corta (F1-ADM-11 G1.2): firmado, atado a un adjunto
/// y a un usuario. El token ES la credencial del endpoint anónimo de descarga.
/// </summary>
public interface IAdjuntoEnlaceTokenService
{
    AdjuntoEnlaceEmitido Emitir(Guid adjuntoId, Guid usuarioId);

    /// <summary>Devuelve los claims, o <c>null</c> si el token es inválido, manipulado o expiró.</summary>
    AdjuntoEnlaceClaims? Validar(string token);
}
