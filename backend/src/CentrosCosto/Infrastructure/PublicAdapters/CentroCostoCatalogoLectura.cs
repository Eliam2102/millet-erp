using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Infrastructure.Persistence;
namespace Millet.CentrosCosto.Infrastructure.PublicAdapters;

internal static class CentroCostoCatalogoLectura
{
    // Los padres también deben estar activos. Lectura histórica conserva los nodos inactivos.
    public static async Task<IReadOnlyList<CentroCostoOpcion>> ObtenerAsync(CentrosCostoDbContext db, CancellationToken ct)
    {
        var d1 = await db.Dim1s.AsNoTracking().ToListAsync(ct);
        var d2 = await db.Dim2s.AsNoTracking().ToListAsync(ct);
        var d3 = await db.Dim3s.AsNoTracking().ToListAsync(ct);
        var plantas = d1.ToDictionary(x => x.Id);
        var areas = d2.ToDictionary(x => x.Id);
        List<CentroCostoOpcion> nodos = [];
        nodos.AddRange(d1.Select(x => new CentroCostoOpcion(x.Id, x.Clave, x.Nombre, 1,
            x.Estatus == EstatusCatalogo.Activo, x.Id, null)));
        nodos.AddRange(d2.Where(x => plantas.ContainsKey(x.Dim1Id)).Select(x => new CentroCostoOpcion(
            x.Id, x.Clave, x.Nombre, 2, x.Estatus == EstatusCatalogo.Activo && plantas[x.Dim1Id].Estatus == EstatusCatalogo.Activo,
            x.Dim1Id, x.Id)));
        nodos.AddRange(d3.Where(x => areas.ContainsKey(x.Dim2Id) && plantas.ContainsKey(areas[x.Dim2Id].Dim1Id))
            .Select(x => new CentroCostoOpcion(x.Id, x.Clave, x.Nombre, 3,
                x.Estatus == EstatusCatalogo.Activo && areas[x.Dim2Id].Estatus == EstatusCatalogo.Activo &&
                plantas[areas[x.Dim2Id].Dim1Id].Estatus == EstatusCatalogo.Activo,
                areas[x.Dim2Id].Dim1Id, x.Dim2Id)));
        return nodos;
    }
}
