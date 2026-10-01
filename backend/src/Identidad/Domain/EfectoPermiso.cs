namespace Millet.Identidad.Domain;

/// <summary>
/// Efecto de una excepción de permiso por usuario sobre la base del rol
/// (ADR-0053): <c>efectivos = (rol ∪ Conceder) \ Denegar</c>.
/// </summary>
public enum EfectoPermiso
{
    /// <summary>Añade un permiso que el rol no trae.</summary>
    Conceder = 1,

    /// <summary>Quita un permiso que el rol sí trae. Gana sobre Conceder.</summary>
    Denegar = 2,
}
