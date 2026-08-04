using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.Asignaciones;

// ============================================================================
// Queries de asignaciones artículo→ubicación (ADR-0047 PR3). Read-only directo
// sobre almacen.asignaciones_articulo_ubicacion; enriquece clave/descripción del
// artículo en batch (ADR-0042), igual que ListarSaldos.
// ============================================================================

public sealed record ListarAsignacionesQuery(
    Guid? UbicacionId,
    Guid? ArticuloId,
    EstatusCatalogo? Estatus,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<AsignacionListItem>>;

public sealed record AsignacionListItem(
    Guid Id,
    Guid UbicacionId,
    Guid ArticuloId,
    EstatusCatalogo Estatus,
    string? ArticuloClave,
    string? ArticuloDescripcion);

public sealed class ListarAsignacionesHandler
    : IRequestHandler<ListarAsignacionesQuery, AlmacenPagedResponse<AsignacionListItem>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ListarAsignacionesHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<AlmacenPagedResponse<AsignacionListItem>> Handle(
        ListarAsignacionesQuery request, CancellationToken cancellationToken)
    {
        var query = _db.AsignacionesArticuloUbicacion.AsNoTracking().AsQueryable();
        if (request.UbicacionId is Guid uid) query = query.Where(a => a.UbicacionId == uid);
        if (request.ArticuloId is Guid aid) query = query.Where(a => a.ArticuloId == aid);
        if (request.Estatus is EstatusCatalogo est) query = query.Where(a => a.Estatus == est);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(a => a.UbicacionId).ThenBy(a => a.ArticuloId)
            .Skip(request.Offset).Take(request.Limit)
            .Select(a => new AsignacionListItem(
                a.Id, a.UbicacionId, a.ArticuloId, a.Estatus,
                null, null))
            .ToListAsync(cancellationToken);

        var articuloIds = items.Select(i => i.ArticuloId).Distinct().ToArray();
        var articulos = articuloIds.Length > 0
            ? await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken)
            : new Dictionary<Guid, ArticuloLectura>();

        var enriquecidos = items
            .Select(i => articulos.TryGetValue(i.ArticuloId, out var art)
                ? i with { ArticuloClave = art.Clave, ArticuloDescripcion = art.Descripcion }
                : i)
            .ToList();

        return new AlmacenPagedResponse<AsignacionListItem>(
            enriquecidos, request.Offset, request.Limit, total);
    }
}

public sealed record ObtenerAsignacionPorIdQuery(Guid Id) : IRequest<AsignacionListItem?>;

public sealed class ObtenerAsignacionPorIdHandler
    : IRequestHandler<ObtenerAsignacionPorIdQuery, AsignacionListItem?>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ObtenerAsignacionPorIdHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<AsignacionListItem?> Handle(
        ObtenerAsignacionPorIdQuery request, CancellationToken cancellationToken)
    {
        var a = await _db.AsignacionesArticuloUbicacion.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (a is null) return null;

        var art = await _articulos.ObtenerAsync(a.ArticuloId, cancellationToken);
        return new AsignacionListItem(
            a.Id, a.UbicacionId, a.ArticuloId, a.Estatus,
            art?.Clave, art?.Descripcion);
    }
}
