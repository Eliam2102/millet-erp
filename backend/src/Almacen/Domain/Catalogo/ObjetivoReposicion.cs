namespace Millet.Almacen.Domain.Catalogo;

/// <summary>
/// Nivel hasta el que se repone una <see cref="AsignacionArticuloUbicacion"/>
/// cuando el stock cruza el punto de reorden (ADR-0047, PR3). El usuario elige
/// uno de los tres niveles de la asignación como destino de la reposición.
/// Lo consume el motor de reorden (PR5).
/// </summary>
public enum ObjetivoReposicion : short
{
    /// <summary>Reponer hasta el nivel mínimo.</summary>
    Minimo = 0,

    /// <summary>Reponer hasta el nivel máximo.</summary>
    Maximo = 1,

    /// <summary>Reponer hasta el punto de reorden.</summary>
    Reorden = 2,
}
