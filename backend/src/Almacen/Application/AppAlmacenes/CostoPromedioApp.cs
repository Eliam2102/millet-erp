using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.AppAlmacenes;

/// <summary>Saldo de una ubicación con la llave de la app (vacía si la ubicación no está en la lista de almacenes).</summary>
public sealed record SaldoCostoApp(
    Guid SucursalId, string? TipoAlmacen, string? Linea, Guid ArticuloId, Guid? VarianteId,
    decimal Cantidad, decimal CostoMxn, DateTimeOffset Fecha);

/// <summary>
/// Costo promedio vigente del ERP en una llave de la app: m² con medida, por cantidad sin medida; MXN. <see cref="Existencia"/>
/// sirve para ponderar si el publicador junta varias llaves (p. ej. dos variantes del ERP con la misma medida A+W).
/// </summary>
public sealed record CostoPromedioApp(
    Guid SucursalId, string TipoAlmacen, string Linea, Guid ArticuloId, Guid? VarianteId, decimal CostoMxn, DateTimeOffset Fecha,
    decimal Existencia = 0m);

/// <summary>
/// Costo promedio del ERP por llave de la app de Almacenes (contrato v9, M-447): sucursal + tipo + línea (M-446) + artículo + medida.
/// El promedio es por ubicación y medida, el mismo para todos sus lotes (E07 S7); con existencia en cero se conserva el último
/// (<see cref="CostoMedio.Vigente"/>). En una sucursal sin lista de almacenes la llave va con tipo y línea vacíos y el costo es el de
/// la sucursal; en una con lista, solo cuentan las ubicaciones de la lista. Nunca cuentan revisión ni transitorio.
/// </summary>
public static class CostoPromedioAppLector
{
    // ponytail: lee todos los saldos en cada ciclo; agregar marca de agua por ultima_actualizacion_at si la tabla crece mucho.
    public static async Task<IReadOnlyList<CostoPromedioApp>> LeerAsync(AlmacenDbContext db, CancellationToken ct)
    {
        var conLlaves = await db.AlmacenesApp.AsNoTracking().Where(a => a.Activo).Select(a => a.SucursalId).Distinct().ToListAsync(ct);
        var saldos = await (from s in db.SaldosInventario.AsNoTracking()
                            join sub in db.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sub.Id
                            join alm in db.Almacenes.AsNoTracking() on sub.AlmacenId equals alm.Id
                            join a in db.AlmacenesApp.AsNoTracking().Where(x => x.Activo) on s.UbicacionId equals a.UbicacionId into llaves
                            from a in llaves.DefaultIfEmpty()
                            where sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                            select new SaldoCostoApp(alm.SucursalId, a == null ? null : a.TipoAlmacen, a == null ? null : a.Linea,
                                s.ArticuloId, s.ProductoAwVarianteId, s.Cantidad, s.CostoPromedioMxn, s.UltimaActualizacionAt))
            .ToListAsync(ct);
        return Agrupar(saldos, conLlaves.ToHashSet());
    }

    public static IReadOnlyList<CostoPromedioApp> Agrupar(IEnumerable<SaldoCostoApp> saldos, IReadOnlySet<Guid> sucursalesConLlaves) =>
        saldos
            .Where(s => s.TipoAlmacen is not null || !sucursalesConLlaves.Contains(s.SucursalId))
            .GroupBy(s => (s.SucursalId, Tipo: s.TipoAlmacen ?? "", Linea: s.Linea ?? "", s.ArticuloId, s.VarianteId))
            .Select(g =>
            {
                var (existencia, promedio) = CostoMedio.Vigente(g.Select(s => (s.Cantidad, s.CostoMxn, s.Fecha)).ToList());
                return new CostoPromedioApp(g.Key.SucursalId, g.Key.Tipo, g.Key.Linea, g.Key.ArticuloId, g.Key.VarianteId,
                    promedio!.Value, g.Max(s => s.Fecha), existencia);
            })
            .ToList();
}
