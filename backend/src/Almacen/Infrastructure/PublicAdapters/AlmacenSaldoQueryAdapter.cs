using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo del puerto público <see cref="IAlmacenSaldoQueryPort"/>
/// (F2-PR2). Read-only sobre <c>almacen.saldos_inventario</c>.
///
/// <para>
/// Convención: respuesta inmediata (sin overhead), <c>AsNoTracking</c>
/// porque otras lecturas cruzadas no requieren tracking. Si en el futuro
/// se requiere consistencia transaccional con la operación llamadora
/// (ej. reservar dentro de la misma TX), se introduce un puerto
/// adicional con semántica de lock — pero <see cref="IAlmacenSaldoQueryPort"/>
/// es solo para lectura sin reservar.
/// </para>
/// </summary>
public sealed class AlmacenSaldoQueryAdapter : IAlmacenSaldoQueryPort
{
    private readonly AlmacenDbContext _db;

    public AlmacenSaldoQueryAdapter(AlmacenDbContext db) => _db = db;

    public async Task<SaldoArticulo?> ConsultarAsync(
        Guid articuloId,
        Guid subAlmacenId,
        CancellationToken cancellationToken)
    {
        // C7.2a: el saldo del sub-almacén = agregado de sus bins. Con N
        // ubicaciones, una sola fila sería parcial y Compras autorizaría RQs
        // con stock incompleto. Espejo de ConsultarTotalArticuloAsync
        // (misma convención: costo ponderado, 0 si cantidad total = 0).
        var agregados = await _db.SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId && s.SubAlmacenId == subAlmacenId)
            .GroupBy(s => s.ArticuloId)
            .Select(g => new
            {
                Cantidad = g.Sum(s => s.Cantidad),
                CantidadDisponible = g.Sum(s => s.CantidadDisponible),
                ValorInventarioMxn = g.Sum(s => s.ValorInventarioMxn),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (agregados is null) return null;

        var costoPromedio = agregados.Cantidad > 0
            ? Math.Round(agregados.ValorInventarioMxn / agregados.Cantidad, 4)
            : 0m;

        return new SaldoArticulo(
            ArticuloId: articuloId,
            SubAlmacenId: subAlmacenId,
            Cantidad: agregados.Cantidad,
            CantidadDisponible: agregados.CantidadDisponible,
            CostoPromedioMxn: costoPromedio,
            ValorInventarioMxn: agregados.ValorInventarioMxn);
    }

    public async Task<SaldoArticulo> ConsultarTotalArticuloAsync(
        Guid articuloId,
        CancellationToken cancellationToken)
    {
        // Suma de cantidades + cantidad_reservada a través de todos los
        // sub-almacenes. Costo promedio ponderado total = sum(cantidad *
        // costo_promedio) / sum(cantidad) — pero esto solo aplica si
        // hay > 0. Si total = 0, devolvemos 0 también.
        var agregados = await _db.SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId)
            .GroupBy(s => s.ArticuloId)
            .Select(g => new
            {
                Cantidad = g.Sum(s => s.Cantidad),
                CantidadDisponible = g.Sum(s => s.CantidadDisponible),
                ValorInventarioMxn = g.Sum(s => s.ValorInventarioMxn),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (agregados is null)
        {
            return new SaldoArticulo(
                articuloId, null, 0m, 0m, 0m, 0m);
        }

        var costoPromedio = agregados.Cantidad > 0
            ? Math.Round(agregados.ValorInventarioMxn / agregados.Cantidad, 4)
            : 0m;

        return new SaldoArticulo(
            ArticuloId: articuloId,
            SubAlmacenId: null,
            Cantidad: agregados.Cantidad,
            CantidadDisponible: agregados.CantidadDisponible,
            CostoPromedioMxn: costoPromedio,
            ValorInventarioMxn: agregados.ValorInventarioMxn);
    }

    public async Task<DisponibilidadArticuloAlmacen> ConsultarDisponibilidadPorAlmacenAsync(
        Guid almacenId,
        Guid articuloId,
        CancellationToken cancellationToken)
    {
        // Rollup a nivel almacén: suma los saldos del artículo en todas las
        // ubicaciones de los sub-almacenes de ese almacén. Almacén resuelve la
        // jerarquía (join saldos→sub_almacenes por el sub_almacen_id
        // denormalizado); el consumidor (Compras) nunca toca la tabla directo.
        var totales = await (
            from s in _db.SaldosInventario.AsNoTracking()
            join sa in _db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sa.Id
            where s.ArticuloId == articuloId && sa.AlmacenId == almacenId
            select s)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cantidad = g.Sum(x => (decimal?)x.Cantidad) ?? 0m,
                Disponible = g.Sum(x => (decimal?)x.CantidadDisponible) ?? 0m,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new DisponibilidadArticuloAlmacen(
            AlmacenId: almacenId,
            ArticuloId: articuloId,
            Cantidad: totales?.Cantidad ?? 0m,
            CantidadDisponible: totales?.Disponible ?? 0m);
    }

    public async Task<DisponibilidadArticuloSucursal> ConsultarDisponibilidadPorSucursalAsync(
        Guid sucursalId,
        Guid articuloId,
        CancellationToken cancellationToken)
    {
        // Rollup a nivel sucursal (N1): join extra sub_almacenes→almacenes filtrando
        // por SucursalId. Suma los saldos del artículo en todas las ubicaciones de
        // todos los almacenes de la sucursal.
        var totales = await (
            from s in _db.SaldosInventario.AsNoTracking()
            join sa in _db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sa.Id
            join al in _db.Almacenes.AsNoTracking() on sa.AlmacenId equals al.Id
            where s.ArticuloId == articuloId && al.SucursalId == sucursalId
            select s)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Cantidad = g.Sum(x => (decimal?)x.Cantidad) ?? 0m,
                Disponible = g.Sum(x => (decimal?)x.CantidadDisponible) ?? 0m,
            })
            .FirstOrDefaultAsync(cancellationToken);

        return new DisponibilidadArticuloSucursal(
            SucursalId: sucursalId,
            ArticuloId: articuloId,
            Cantidad: totales?.Cantidad ?? 0m,
            CantidadDisponible: totales?.Disponible ?? 0m);
    }
}
