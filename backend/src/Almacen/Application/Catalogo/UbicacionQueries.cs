using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.Catalogo;

// ============================================================================
// Query de ubicaciones (N4) para el FE de asignación artículo→ubicación
// (ADR-0047 PR C). Read-only directo sobre almacen.ubicaciones; enriquece
// clave/nombre del sub-almacén (N3) y del almacén (N2) padres con join
// in-context (todo vive en AlmacenDbContext) — esencial para distinguir las
// ubicaciones "ÚNICA" (una por sub-almacén, misma clave). Molde:
// ListarSubAlmacenesQuery.
// ============================================================================

public sealed record ListarUbicacionesQuery(
    Guid? SubAlmacenId,
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<UbicacionListItem>>;

public sealed record UbicacionListItem(
    Guid Id,
    Guid SubAlmacenId,
    string Clave,
    string Nombre,
    EstatusCatalogo Estatus,
    bool EsDefault,
    string SubAlmacenClave,
    string SubAlmacenNombre,
    string AlmacenClave,
    string AlmacenNombre);

public sealed class ListarUbicacionesHandler
    : IRequestHandler<ListarUbicacionesQuery, AlmacenPagedResponse<UbicacionListItem>>
{
    private readonly AlmacenDbContext _db;

    public ListarUbicacionesHandler(AlmacenDbContext db) => _db = db;

    public async Task<AlmacenPagedResponse<UbicacionListItem>> Handle(
        ListarUbicacionesQuery request, CancellationToken cancellationToken)
    {
        // Join in-context Ubicación (N4) → SubAlmacén (N3) → Almacén (N2) para
        // traer clave/nombre de los padres (no hay read-port; mismo DbContext).
        var query =
            from u in _db.Ubicaciones.AsNoTracking()
            join sa in _db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            join al in _db.Almacenes.AsNoTracking() on sa.AlmacenId equals al.Id
            select new { u, sa, al };

        if (request.SubAlmacenId is Guid subId)
            query = query.Where(x => x.u.SubAlmacenId == subId);
        if (request.Estatus is EstatusCatalogo est)
            query = query.Where(x => x.u.Estatus == est);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim();
            query = query.Where(x =>
                x.u.Clave.Contains(q) || x.u.Nombre.Contains(q) ||
                x.sa.Clave.Contains(q) || x.sa.Nombre.Contains(q));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.al.Clave).ThenBy(x => x.sa.Clave).ThenBy(x => x.u.Clave)
            .Skip(request.Offset).Take(request.Limit)
            .Select(x => new UbicacionListItem(
                x.u.Id, x.u.SubAlmacenId, x.u.Clave, x.u.Nombre, x.u.Estatus, x.u.EsDefault,
                x.sa.Clave, x.sa.Nombre, x.al.Clave, x.al.Nombre))
            .ToListAsync(cancellationToken);

        return new AlmacenPagedResponse<UbicacionListItem>(
            items, request.Offset, request.Limit, total);
    }
}
