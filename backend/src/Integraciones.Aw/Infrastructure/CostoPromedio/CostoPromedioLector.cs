using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Integraciones.Aw.Infrastructure.CostoPromedio;

/// <summary>
/// Costo promedio vigente (M-447) de cada artículo A+W en cada almacén, leído de los saldos del ERP (<c>almacen.saldos_inventario</c>,
/// que ya trae el promedio móvil por m² o por pieza). Llave: artículo A+W + sucursal A+W + almacén de la lista (tipo + línea) + medida de
/// hoja (sin orientación). Todos los lotes y variantes de esa medida en ese almacén comparten el costo (<see cref="CostoMedio.Vigente"/>: ponderado por existencia; con
/// existencia cero conserva el último). Una sucursal sin lista de almacenes publica un solo costo (tipo y línea ""). Los saldos en
/// revisión o tránsito no cuentan, igual que en <c>CostoMedioReader</c>. Solo artículos ligados a A+W con clave numérica.
/// </summary>
public sealed class CostoPromedioLector(AlmacenDbContext almacen, CompartidoDbContext maestros, ILogger<CostoPromedioLector> logger)
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

        var entradas = saldos
            .Select(s =>
            {
                // Con lista: solo cuentan las ubicaciones de la lista; sin lista: la sucursal entera.
                var enLista = lista.FirstOrDefault(l => l.UbicacionId == s.UbicacionId);
                var sucursalTieneLista = lista.Any(l => l.SucursalId == s.SucursalId);
                if (enLista is null && sucursalTieneLista) return null;
                if (!sucursales.TryGetValue(s.SucursalId, out var suc)) return null;
                var v = s.ProductoAwVarianteId is Guid vid && variantes.TryGetValue(vid, out var vv) && vv.AnchoMm > 0 && vv.AltoMm > 0 ? vv : null;
                return new SaldoParaCosto(claveAw[s.ArticuloId], suc, enLista?.TipoAlmacen ?? "", enLista?.Linea ?? "",
                    v is null ? null : (int)Math.Round(v.AnchoMm!.Value), v is null ? null : (int)Math.Round(v.AltoMm!.Value),
                    s.Cantidad, s.CostoPromedioMxn, s.UltimaActualizacionAt);
            })
            .OfType<SaldoParaCosto>();

        var filas = new List<CostoPromedioFila>();
        foreach (var f in Agrupar(entradas))
        {
            if (CabeEnContrato(f)) { filas.Add(f); continue; }
            logger.LogWarning("[CostoPromedio] Llave fuera del contrato (sucursal ≤ {Suc}, tipo/línea ≤ {Tl} caracteres), no se publica: "
                + "artículo {Art}, sucursal '{Sucursal}', tipo '{Tipo}', línea '{Linea}'.",
                MaxSucursal, MaxTipoLinea, f.ArticuloAw, f.Sucursal, f.AlmacenTipo, f.Linea);
        }
        return filas;
    }

    /// <summary>Largo de las columnas de <c>dbo.erp_costo_promedio</c> (script 19 enviado a TI): sucursal nvarchar(30), tipo y línea nvarchar(50).</summary>
    public const int MaxSucursal = 30, MaxTipoLinea = 50;

    /// <summary>
    /// Una fila por llave del contrato (artículo A+W + sucursal + tipo + línea + medida). La medida va sin orientación: ancho = lado
    /// menor, alto = lado mayor (2600×1800 y 1800×2600 son la misma hoja). Todos los saldos de la llave (lotes, variantes del ERP con la
    /// misma medida) se juntan en un solo promedio ponderado por existencia (<see cref="CostoMedio.Vigente"/>; existencia cero: el último).
    /// </summary>
    public static IReadOnlyList<CostoPromedioFila> Agrupar(IEnumerable<SaldoParaCosto> saldos)
    {
        var filas = new List<CostoPromedioFila>();
        foreach (var g in saldos.GroupBy(s => (s.ArticuloAw, s.Sucursal, s.AlmacenTipo, s.Linea,
                     Ancho: s.AnchoMm is int a && s.AltoMm is int b ? Math.Min(a, b) : (int?)null,
                     Alto: s.AnchoMm is int a2 && s.AltoMm is int b2 ? Math.Max(a2, b2) : (int?)null)))
        {
            var (_, promedio) = CostoMedio.Vigente(g.Select(s => (s.Cantidad, s.Costo, s.Fecha)).ToList());
            if (promedio is null) continue;
            var (art, suc, tipo, linea, ancho, alto) = g.Key;
            var vidrio = ancho is not null;
            filas.Add(new CostoPromedioFila(art, suc, tipo, linea, ancho, alto, vidrio ? promedio : null, vidrio ? null : promedio));
        }
        return filas;
    }

    /// <summary>La llave cabe en las columnas del destino; una llave recortada nunca coincidiría con la consulta de la app.</summary>
    public static bool CabeEnContrato(CostoPromedioFila f) =>
        f.Sucursal.Length <= MaxSucursal && f.AlmacenTipo.Length <= MaxTipoLinea && f.Linea.Length <= MaxTipoLinea;
}

/// <summary>Un saldo del ERP ya traducido a la llave de la app (medida tal como viene de la variante).</summary>
public sealed record SaldoParaCosto(
    int ArticuloAw, string Sucursal, string AlmacenTipo, string Linea, int? AnchoMm, int? AltoMm, decimal Cantidad, decimal Costo, DateTimeOffset Fecha);
