using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Proyección compartida del selector "Máquina" (Dim3). La usan las DOS
// variantes del selector de la Fase E (ADR-0050): BuscarDim3Query (FILTRADO
// por alcance, para la RQ) y BuscarDim3AbiertoQuery (ABIERTO, para la captura
// por proxy: vale + línea manual de OC). Ambas comparten el join local a
// GrupoDim3/Dim2/Dim1 (contexto completo, sin puertos externos), el default
// solo-activas con opt-in de inactivas, el filtro q (clave O nombre) y el
// top-N. La ÚNICA diferencia es el `baseDim3s` que cada una pasa: con o sin
// el AplicarA(alcance). Extraído para que las dos proyecciones no dricten.
// ============================================================================

internal static class Dim3SelectorProjection
{
    public const int LimitMax = 100;

    /// <summary>
    /// Aplica el join de contexto + filtros + top-N sobre el conjunto base de
    /// Dim3 que recibe (ya filtrado o no por alcance) y proyecta a
    /// <see cref="Dim3BusquedaItem"/>.
    /// </summary>
    public static async Task<IReadOnlyList<Dim3BusquedaItem>> EjecutarAsync(
        CentrosCostoDbContext db,
        IQueryable<Dim3> baseDim3s,
        string? q,
        bool incluirInactivas,
        int limit,
        CancellationToken cancellationToken)
    {
        var tope = Math.Clamp(limit, 1, LimitMax);

        var query =
            from e in baseDim3s
            join s in db.GruposDim3.AsNoTracking() on e.GrupoDim3Id equals s.Id
            join d in db.Dim2s.AsNoTracking() on e.Dim2Id equals d.Id
            join u in db.Dim1s.AsNoTracking() on d.Dim1Id equals u.Id
            select new { e, GrupoNombre = s.Nombre, Dim2 = d, Dim1 = u };

        if (!incluirInactivas)
            query = query.Where(x => x.e.Estatus == EstatusCatalogo.Activo);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var patron = $"%{q.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.e.Clave, patron) || EF.Functions.ILike(x.e.Nombre, patron));
        }

        return await query
            .OrderBy(x => x.e.Clave)
            .Take(tope)
            .Select(x => new Dim3BusquedaItem(
                x.e.Id, x.e.Clave, x.e.Nombre, x.GrupoNombre,
                x.Dim2.Clave, x.Dim2.Nombre,
                x.Dim1.Clave, x.Dim1.Nombre,
                x.e.Estatus))
            .ToListAsync(cancellationToken);
    }
}
