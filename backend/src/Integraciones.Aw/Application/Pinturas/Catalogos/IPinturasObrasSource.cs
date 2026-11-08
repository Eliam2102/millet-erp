namespace Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

/// <summary>
/// Fuente para obtener el catálogo de obras a sincronizar/publicar hacia Planta Pintura (<c>dbo.pinturas_obra</c>).
/// Permite integrar obras compartidas desde A+W (KA_OBJEKT) u otras fuentes de datos maestros.
/// </summary>
public interface IPinturasObrasSource
{
    /// <summary>
    /// Obtiene la lista de obras a sincronizar.
    /// </summary>
    Task<IReadOnlyList<FilaPinturasObra>> ObtenerObrasAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Implementación inerte (no-op) por omisión que devuelve una lista vacía de obras.
/// </summary>
public sealed class NoOpPinturasObrasSource : IPinturasObrasSource
{
    public static readonly NoOpPinturasObrasSource Instance = new();

    public Task<IReadOnlyList<FilaPinturasObra>> ObtenerObrasAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FilaPinturasObra>>([]);
}
