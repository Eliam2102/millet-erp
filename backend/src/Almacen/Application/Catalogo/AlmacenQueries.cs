using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.Catalogo;

// ============================================================================
// Queries del catálogo Almacén / SubAlmacén (F1-PR1).
// Paginado simple — el patrón master-detail de UI consume estos endpoints.
// ============================================================================

public sealed record AlmacenListItem(
    Guid Id,
    string Clave,
    string Nombre,
    Guid SucursalId,
    EstatusCatalogo Estatus);

public sealed record AlmacenDetalle(
    Guid Id,
    string Clave,
    string Nombre,
    Guid SucursalId,
    EstatusCatalogo Estatus,
    int Version,
    IReadOnlyList<SubAlmacenListItem> SubAlmacenes);

public sealed record SubAlmacenListItem(
    Guid Id,
    Guid AlmacenId,
    string Clave,
    string Nombre,
    TipoSubAlmacen Tipo,
    EstatusCatalogo Estatus);

public sealed record AlmacenPagedResponse<T>(
    IReadOnlyList<T> Items,
    int Offset,
    int Limit,
    int Total);

// ─── Listar Almacenes ─────────────────────────────────────────────────────────

public sealed record ListarAlmacenesQuery(
    EstatusCatalogo? Estatus,
    Guid? SucursalId,
    string? Q,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<AlmacenListItem>>;

public sealed class ListarAlmacenesHandler
    : IRequestHandler<ListarAlmacenesQuery, AlmacenPagedResponse<AlmacenListItem>>
{
    private readonly AlmacenDbContext _db;

    public ListarAlmacenesHandler(AlmacenDbContext db) => _db = db;

    public async Task<AlmacenPagedResponse<AlmacenListItem>> Handle(
        ListarAlmacenesQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Domain.Catalogo.Almacen> query = _db.Almacenes.AsNoTracking();
        if (request.Estatus is EstatusCatalogo e) query = query.Where(a => a.Estatus == e);
        if (request.SucursalId is Guid sid) query = query.Where(a => a.SucursalId == sid);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q;
            query = query.Where(a => a.Clave.Contains(q) || a.Nombre.Contains(q));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(a => a.Clave)
            .Skip(request.Offset).Take(request.Limit)
            .Select(a => new AlmacenListItem(a.Id, a.Clave, a.Nombre, a.SucursalId, a.Estatus))
            .ToListAsync(cancellationToken);

        return new AlmacenPagedResponse<AlmacenListItem>(items, request.Offset, request.Limit, total);
    }
}

// ─── Obtener Almacén por Id (con sub-almacenes) ──────────────────────────────

public sealed record ObtenerAlmacenPorIdQuery(Guid Id) : IRequest<AlmacenDetalle?>;

public sealed class ObtenerAlmacenPorIdHandler
    : IRequestHandler<ObtenerAlmacenPorIdQuery, AlmacenDetalle?>
{
    private readonly AlmacenDbContext _db;

    public ObtenerAlmacenPorIdHandler(AlmacenDbContext db) => _db = db;

    public async Task<AlmacenDetalle?> Handle(
        ObtenerAlmacenPorIdQuery request, CancellationToken cancellationToken)
    {
        var almacen = await _db.Almacenes.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.Id, cancellationToken);
        if (almacen is null) return null;

        var subs = await _db.SubAlmacenes.AsNoTracking()
            .Where(s => s.AlmacenId == request.Id)
            .OrderBy(s => s.Clave)
            .Select(s => new SubAlmacenListItem(s.Id, s.AlmacenId, s.Clave, s.Nombre, s.Tipo, s.Estatus))
            .ToListAsync(cancellationToken);

        return new AlmacenDetalle(
            almacen.Id, almacen.Clave, almacen.Nombre, almacen.SucursalId,
            almacen.Estatus, almacen.Version, subs);
    }
}

// ─── Listar SubAlmacenes ─────────────────────────────────────────────────────

public sealed record ListarSubAlmacenesQuery(
    Guid? AlmacenId,
    TipoSubAlmacen? Tipo,
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<SubAlmacenListItem>>;

public sealed class ListarSubAlmacenesHandler
    : IRequestHandler<ListarSubAlmacenesQuery, AlmacenPagedResponse<SubAlmacenListItem>>
{
    private readonly AlmacenDbContext _db;

    public ListarSubAlmacenesHandler(AlmacenDbContext db) => _db = db;

    public async Task<AlmacenPagedResponse<SubAlmacenListItem>> Handle(
        ListarSubAlmacenesQuery request, CancellationToken cancellationToken)
    {
        IQueryable<SubAlmacen> query = _db.SubAlmacenes.AsNoTracking();
        if (request.AlmacenId is Guid aid) query = query.Where(s => s.AlmacenId == aid);
        if (request.Tipo is TipoSubAlmacen t) query = query.Where(s => s.Tipo == t);
        if (request.Estatus is EstatusCatalogo e) query = query.Where(s => s.Estatus == e);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q;
            query = query.Where(s => s.Clave.Contains(q) || s.Nombre.Contains(q));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(s => s.Clave)
            .Skip(request.Offset).Take(request.Limit)
            .Select(s => new SubAlmacenListItem(s.Id, s.AlmacenId, s.Clave, s.Nombre, s.Tipo, s.Estatus))
            .ToListAsync(cancellationToken);

        return new AlmacenPagedResponse<SubAlmacenListItem>(items, request.Offset, request.Limit, total);
    }
}
