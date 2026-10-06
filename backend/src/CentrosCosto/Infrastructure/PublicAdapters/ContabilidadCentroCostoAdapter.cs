using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Domain;

namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

/// <summary>
/// Adaptador de <see cref="ICentroCostoContabilidadPort"/> (F1-CON-02): CentrosCosto, dueño del dato, sirve a Contabilidad
/// el árbol Dim1 → Dim2 → Dim3 (mismos IDs de ADM-08). Sin filtro de alcance de usuario: el alcance contable es por
/// sucursal y lo resuelve Contabilidad. Por id incluye inactivos ("ver ≠ elegir", ADR-0050).
/// </summary>
public sealed class ContabilidadCentroCostoAdapter(CentrosCostoDbContext db) : ICentroCostoContabilidadPort
{
    public async Task<IReadOnlyDictionary<Guid, CentroCostoNodo>> ObtenerAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<Guid, CentroCostoNodo>();
        var arr = ids.Distinct().ToArray();
        var d3 = await db.Dim3s.AsNoTracking().Where(x => arr.Contains(x.Id)).ToListAsync(ct);
        var d2Ids = arr.Concat(d3.Select(x => x.Dim2Id)).Distinct().ToArray();
        var d2 = await db.Dim2s.AsNoTracking().Where(x => d2Ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var d1Ids = arr.Concat(d2.Values.Select(x => x.Dim1Id)).Distinct().ToArray();
        var d1 = await db.Dim1s.AsNoTracking().Where(x => d1Ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var res = new Dictionary<Guid, CentroCostoNodo>();
        foreach (var id in arr)
        {
            if (d1.TryGetValue(id, out var a))
            {
                var activo = a.Estatus == EstatusCatalogo.Activo;
                res[id] = new(a.Id, DimensionContable.Dim1, a.Clave, a.Nombre, activo, activo, a.Id, null, a.Clave, null);
            }
            else if (d2.TryGetValue(id, out var b) && d1.TryGetValue(b.Dim1Id, out var pb))
            {
                var activo = b.Estatus == EstatusCatalogo.Activo;
                res[id] = new(b.Id, DimensionContable.Dim2, b.Clave, b.Nombre, activo,
                    activo && pb.Estatus == EstatusCatalogo.Activo, pb.Id, b.Id, pb.Clave, b.Clave);
            }
            else if (d3.FirstOrDefault(x => x.Id == id) is { } c && d2.TryGetValue(c.Dim2Id, out var pc) && d1.TryGetValue(pc.Dim1Id, out var ac))
            {
                var activo = c.Estatus == EstatusCatalogo.Activo;
                res[id] = new(c.Id, DimensionContable.Dim3, c.Clave, c.Nombre, activo,
                    activo && pc.Estatus == EstatusCatalogo.Activo && ac.Estatus == EstatusCatalogo.Activo, ac.Id, pc.Id, ac.Clave, pc.Clave);
            }
        }
        return res;
    }

    public async Task<IReadOnlyList<CentroCostoNodo>> BuscarAsync(
        DimensionContable nivel, string? q, AlcanceCentros? alcance, bool incluirInactivos, int limite, CancellationToken ct)
    {
        var patron = string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
        var sinAlcance = alcance is null;
        var dim1 = alcance?.Dim1Ids.ToArray() ?? [];
        var dim2 = alcance?.Dim2Ids.ToArray() ?? [];
        // Alcance = centros de las ubicaciones permitidas UNIDOS a los CeCo permitidos (corporativos) y sus equipos.
        List<Guid> ids = nivel switch
        {
            DimensionContable.Dim1 => await db.Dim1s.AsNoTracking()
                .Where(x => sinAlcance || dim1.Contains(x.Id) || db.Dim2s.Any(d => d.Dim1Id == x.Id && dim2.Contains(d.Id)))
                .Where(x => incluirInactivos || x.Estatus == EstatusCatalogo.Activo)
                .Where(x => patron == null || EF.Functions.ILike(x.Clave, patron) || EF.Functions.ILike(x.Nombre, patron))
                .OrderBy(x => x.Clave).Take(limite).Select(x => x.Id).ToListAsync(ct),
            DimensionContable.Dim2 => await db.Dim2s.AsNoTracking()
                .Where(x => sinAlcance || dim1.Contains(x.Dim1Id) || dim2.Contains(x.Id))
                .Where(x => incluirInactivos || x.Estatus == EstatusCatalogo.Activo)
                .Where(x => patron == null || EF.Functions.ILike(x.Clave, patron) || EF.Functions.ILike(x.Nombre, patron))
                .OrderBy(x => x.Clave).Take(limite).Select(x => x.Id).ToListAsync(ct),
            _ => await db.Dim3s.AsNoTracking()
                .Where(x => sinAlcance || dim2.Contains(x.Dim2Id) || db.Dim2s.Any(d => d.Id == x.Dim2Id && dim1.Contains(d.Dim1Id)))
                .Where(x => incluirInactivos || x.Estatus == EstatusCatalogo.Activo)
                .Where(x => patron == null || EF.Functions.ILike(x.Clave, patron) || EF.Functions.ILike(x.Nombre, patron))
                .OrderBy(x => x.Clave).Take(limite).Select(x => x.Id).ToListAsync(ct),
        };
        var nodos = await ObtenerAsync(ids, ct);
        return [.. ids.Where(nodos.ContainsKey).Select(id => nodos[id])];
    }
}
