namespace Millet.SharedKernel.Application.Correo;

/// <summary>
/// Puerto para resolución del destinatario de avisos y notificaciones a usuarios (86e3zexqm).
/// Evalúa prioritariamente <c>correo_para_avisos</c> si está definido (no nulo ni vacío);
/// si no, hace fallback al correo principal de Entra ID (<c>email</c>).
/// </summary>
public interface IUsuarioCorreoAvisosPort
{
    /// <summary>Resuelve el correo de destino para avisos del usuario especificado.</summary>
    Task<string?> ObtenerCorreoAvisosAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>Resuelve en lote los correos de destino para avisos de los usuarios especificados.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ObtenerCorreosAvisosAsync(IReadOnlyCollection<Guid> usuarioIds, CancellationToken cancellationToken);
}

/// <summary>
/// Lógica canónica de resolución de correo para avisos (86e3zexqm).
/// </summary>
public static class UsuarioAvisosResolver
{
    /// <summary>
    /// Resuelve el correo de avisos evaluando prioritariamente <paramref name="correoParaAvisos"/>
    /// si está definido (no nulo ni vacío tras recorte); si no, utiliza <paramref name="correoPrincipal"/>.
    /// Devuelve null si ambos son nulos o vacíos.
    /// </summary>
    public static string? ResolverCorreoDestino(string? correoParaAvisos, string? correoPrincipal)
    {
        if (!string.IsNullOrWhiteSpace(correoParaAvisos))
            return correoParaAvisos.Trim();

        return string.IsNullOrWhiteSpace(correoPrincipal) ? null : correoPrincipal.Trim();
    }
}
