using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

internal sealed class AlmacenRevisionFake : IAlmacenRevisionReadPort
{
    public HashSet<Guid> EnRevision { get; } = [];

    public Task<IReadOnlySet<Guid>> FiltrarSubAlmacenesEnRevisionAsync(
        IReadOnlyCollection<Guid> subAlmacenIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(subAlmacenIds.Where(EnRevision.Contains).ToHashSet());
}

internal sealed class DependenciasFake(bool conAlmacen = true) : IDependenciaRevisoraReadPort
{
    public static readonly Guid AlmacenId = Guid.Parse("00000005-0004-0000-0000-000000000002");

    private readonly IReadOnlyList<DependenciaRevisoraDto> _lista = conAlmacen
        ? [new(AlmacenId, "ALMACEN", "Almacén No-Producción", true)]
        : [];

    public Task<IReadOnlyList<DependenciaRevisoraDto>> ListarAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_lista);

    public Task<DependenciaRevisoraDto?> ObtenerAsync(Guid dependenciaId, CancellationToken cancellationToken) =>
        Task.FromResult(_lista.FirstOrDefault(d => d.Id == dependenciaId));
}
