using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>
/// Costo promedio vigente (M-447) de cada artículo A+W en cada almacén, leído de los saldos del ERP (<c>almacen.saldos_inventario</c>,
/// que ya trae el promedio móvil por m² o por pieza). Llave: artículo A+W + sucursal A+W + almacén de la lista (tipo + línea) + medida de
/// hoja. Todos los lotes de esa medida en ese almacén comparten el costo (<see cref="CostoMedio.Vigente"/>: ponderado por existencia; con
/// existencia cero conserva el último). Una sucursal sin lista de almacenes publica un solo costo (tipo y línea ""). Los saldos en
/// revisión o tránsito no cuentan, igual que en <c>CostoMedioReader</c>. Solo artículos ligados a A+W con clave numérica.
/// </summary>
public sealed class CostoPromedioLector(AlmacenDbContext almacen, CompartidoDbContext maestros)
{
    public async Task<IReadOnlyList<CostoPromedioFila>> LeerAsync(CancellationToken ct)
    {
        var articulos = await (from a in maestros.Articulos.AsNoTracking()
                               join p in maestros.ProductosAw.AsNoTracking() on a.ProductoAwId equals p.Id
                               select new { a.Id, p.ReferenciaExterna }).ToListAsync(ct);
        var claveAw = articulos.Select(a => (a.Id, Ok: int.TryParse(a.ReferenciaExterna, out var n), N: n))
            .Where(a => a.Ok).ToDictionary(a => a.Id, a => a.N);
        if (claveAw.Count == 0) return [];
        var ids = claveAw.Keys.ToList();

        var saldos = await (from s in almacen.SaldosInventario.AsNoTracking()
                            join sub in almacen.SubAlmacenes.AsNoTracking() on s.SubAlmacenId equals sub.Id
                            join alm in almacen.Almacenes.AsNoTracking() on sub.AlmacenId equals alm.Id
                            where ids.Contains(s.ArticuloId)
                                && sub.Tipo != TipoSubAlmacen.MaterialEnRevision && sub.Tipo != TipoSubAlmacen.Transitorio
                            select new { s.ArticuloId, s.ProductoAwVarianteId, s.UbicacionId, alm.SucursalId, s.Cantidad, s.CostoPromedioMxn, s.UltimaActualizacionAt })
            .ToListAsync(ct);
        var lista = await almacen.AlmacenesApp.AsNoTracking().Where(a => a.Activo)
            .Select(a => new { a.SucursalId, a.TipoAlmacen, a.Linea, a.UbicacionId }).ToListAsync(ct);
        var sucursales = await maestros.Sucursales.AsNoTracking()
            .Select(x => new { x.Id, Clave = x.ClaveAw ?? x.Clave }).ToDictionaryAsync(x => x.Id, x => x.Clave, ct);
        var variantes = await maestros.ProductosAwVariantes.AsNoTracking()
            .Select(v => new { v.Id, v.AnchoMm, v.AltoMm }).ToDictionaryAsync(v => v.Id, ct);

        var grupos = saldos
            .Select(s =>
            {
                // Con lista: solo cuentan las ubicaciones de la lista; sin lista: la sucursal entera.
                var enLista = lista.FirstOrDefault(l => l.UbicacionId == s.UbicacionId);
                var sucursalTieneLista = lista.Any(l => l.SucursalId == s.SucursalId);
                if (enLista is null && sucursalTieneLista) return null;
                if (!sucursales.TryGetValue(s.SucursalId, out var suc)) return null;
                var v = s.ProductoAwVarianteId is Guid vid && variantes.TryGetValue(vid, out var vv) && vv.AnchoMm > 0 && vv.AltoMm > 0 ? vv : null;
                return new
                {
                    Llave = (claveAw[s.ArticuloId], suc, enLista?.TipoAlmacen ?? "", enLista?.Linea ?? "",
                        v is null ? (int?)null : (int)Math.Round(v.AnchoMm!.Value), v is null ? (int?)null : (int)Math.Round(v.AltoMm!.Value)),
                    Saldo = (s.Cantidad, s.CostoPromedioMxn, s.UltimaActualizacionAt),
                };
            })
            .Where(x => x is not null)
            .GroupBy(x => x!.Llave);

        var filas = new List<CostoPromedioFila>();
        foreach (var g in grupos)
        {
            var (_, promedio) = CostoMedio.Vigente(g.Select(x => x!.Saldo).ToList());
            if (promedio is null) continue;
            var (art, suc, tipo, linea, ancho, alto) = g.Key;
            var vidrio = ancho is not null;
            filas.Add(new CostoPromedioFila(art, suc, tipo, linea, ancho, alto, vidrio ? promedio : null, vidrio ? null : promedio));
        }
        return filas;
    }
}
