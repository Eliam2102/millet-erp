namespace Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

/// <summary>
/// Implementación inerte (no-op) de <see cref="IPinturasCatalogosPublisher"/>.
/// Utilizada en entornos donde la base de integración (<c>AwIntegracionDb</c>) no está configurada
/// o cuando el publicador está deshabilitado.
/// </summary>
public sealed class NoOpPinturasCatalogosPublisher : IPinturasCatalogosPublisher
{
    public static readonly NoOpPinturasCatalogosPublisher Instance = new();

    public Task<PinturasPublicacionResultado> PublicarAsync(PinturasCatalogosBatch batch, CancellationToken cancellationToken)
    {
        return Task.FromResult(PinturasPublicacionResultado.Vacio());
    }
}
