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

internal sealed class DependenciasFake(bool conCompras = true) : IDependenciaRevisoraReadPort
{
    public static readonly Guid ComprasId = Guid.Parse("00000005-0004-0000-0000-000000000001");

    private readonly IReadOnlyList<DependenciaRevisoraDto> _lista = conCompras
        ? [new(ComprasId, "COMPRAS", "Compras", true)]
        : [];

    public Task<IReadOnlyList<DependenciaRevisoraDto>> ListarAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_lista);

    public Task<DependenciaRevisoraDto?> ObtenerAsync(Guid dependenciaId, CancellationToken cancellationToken) =>
        Task.FromResult(_lista.FirstOrDefault(d => d.Id == dependenciaId));
}
