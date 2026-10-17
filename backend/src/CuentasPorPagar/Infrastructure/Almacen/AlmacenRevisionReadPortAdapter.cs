using Millet.Almacen.Domain.Ports.Externos;
using Millet.CuentasPorPagar.Domain.Ports.Almacen;

namespace Millet.CuentasPorPagar.Infrastructure.Almacen;

/// <summary>
/// Adapter de <see cref="IAlmacenRevisionReadPort"/>: delega en el Open Host Service
/// de Almacén (<see cref="IAlmacenSaldoQueryPort"/>); CxP no lee tablas de Almacén.
/// </summary>
public sealed class AlmacenRevisionReadPortAdapter(IAlmacenSaldoQueryPort saldos) : IAlmacenRevisionReadPort
{
    public Task<IReadOnlySet<Guid>> FiltrarSubAlmacenesEnRevisionAsync(
        IReadOnlyCollection<Guid> subAlmacenIds,
        CancellationToken cancellationToken) =>
        saldos.FiltrarSubAlmacenesEnRevisionAsync(subAlmacenIds, cancellationToken);
}
