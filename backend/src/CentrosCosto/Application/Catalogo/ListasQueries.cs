using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Application.Common;
using Millet.CentrosCosto.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Listas planas por nivel (CECO-PR4) para los modales del admin (Fase C).
// Molde: ListarUbicacionesQuery de Almacén — filtros por padre/estatus/q,
// paginación offset/limit con Total, y padres/grupos resueltos en el DTO
// (ADR-0042). Todo local al esquema. La búsqueda del selector de Dim3 vive
// en BuscarDim3Query (contrato distinto: top-N con contexto multi-nivel y
// filtro de alcance en CECO-PR6).
// ============================================================================

// ─── Dim1 ────────────────────────────────────────────────────────────────────

public sealed record ListarDim1Query(
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<PagedResponse<Dim1Response>>;

public sealed class ListarDim1Handler
    : IRequestHandler<ListarDim1Query, PagedResponse<Dim1Response>>
{
    private readonly CentrosCostoDbContext _db;

    public ListarDim1Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<PagedResponse<Dim1Response>> Handle(
        ListarDim1Query request, CancellationToken cancellationToken)
    {
        var query = _db.Dim1s.AsNoTracking().AsQueryable();
        if (request.Estatus is EstatusCatalogo estatus)
            query = query.Where(d => d.Estatus == estatus);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = $"%{request.Q.Trim()}%";
            query = query.Where(d =>
                EF.Functions.ILike(d.Clave, q) || EF.Functions.ILike(d.Nombre, q));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(d => d.Clave)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(d => new Dim1Response(d.Id, d.Clave, d.Nombre, d.Estatus, d.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<Dim1Response>(items, total, request.Offset, request.Limit);
    }
}

// ─── Grupos (GrupoDim2 / GrupoDim3 — misma forma) ────────────────────────────

public sealed record ListarGruposDim2Query(
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<PagedResponse<GrupoDimResponse>>;

public sealed class ListarGruposDim2Handler
    : IRequestHandler<ListarGruposDim2Query, PagedResponse<GrupoDimResponse>>
{
    private readonly CentrosCostoDbContext _db;

    public ListarGruposDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<PagedResponse<GrupoDimResponse>> Handle(
        ListarGruposDim2Query request, CancellationToken cancellationToken)
    {
        var query = _db.GruposDim2.AsNoTracking().AsQueryable();
        if (request.Estatus is EstatusCatalogo estatus)
            query = query.Where(g => g.Estatus == estatus);
        if (!string.IsNullOrWhiteSpace(request.Q))
            query = query.Where(g => EF.Functions.ILike(g.Nombre, $"%{request.Q.Trim()}%"));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(g => g.Nombre)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(g => new GrupoDimResponse(g.Id, g.Nombre, g.Estatus, g.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<GrupoDimResponse>(items, total, request.Offset, request.Limit);
    }
}

public sealed record ListarGruposDim3Query(
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<PagedResponse<GrupoDimResponse>>;

public sealed class ListarGruposDim3Handler
    : IRequestHandler<ListarGruposDim3Query, PagedResponse<GrupoDimResponse>>
{
    private readonly CentrosCostoDbContext _db;

    public ListarGruposDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<PagedResponse<GrupoDimResponse>> Handle(
        ListarGruposDim3Query request, CancellationToken cancellationToken)
    {
        var query = _db.GruposDim3.AsNoTracking().AsQueryable();
        if (request.Estatus is EstatusCatalogo estatus)
            query = query.Where(g => g.Estatus == estatus);
        if (!string.IsNullOrWhiteSpace(request.Q))
            query = query.Where(g => EF.Functions.ILike(g.Nombre, $"%{request.Q.Trim()}%"));

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(g => g.Nombre)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(g => new GrupoDimResponse(g.Id, g.Nombre, g.Estatus, g.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<GrupoDimResponse>(items, total, request.Offset, request.Limit);
    }
}

// ─── Dim2 ────────────────────────────────────────────────────────────────────

public sealed record Dim2ListItem(
    Guid Id,
    Guid Dim1Id,
    string Clave,
    string Nombre,
    Guid GrupoDim2Id,
    string GrupoDim2Nombre,
    EstatusCatalogo Estatus,
    int Version);

public sealed record ListarDim2Query(
    Guid? Dim1Id,
    Guid? GrupoDim2Id,
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<PagedResponse<Dim2ListItem>>;

public sealed class ListarDim2Handler
    : IRequestHandler<ListarDim2Query, PagedResponse<Dim2ListItem>>
{
    private readonly CentrosCostoDbContext _db;

    public ListarDim2Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<PagedResponse<Dim2ListItem>> Handle(
        ListarDim2Query request, CancellationToken cancellationToken)
    {
        var query = _db.Dim2s.AsNoTracking().AsQueryable();
        if (request.Dim1Id is Guid dim1Id)
            query = query.Where(d => d.Dim1Id == dim1Id);
        if (request.GrupoDim2Id is Guid grupoId)
            query = query.Where(d => d.GrupoDim2Id == grupoId);
        if (request.Estatus is EstatusCatalogo estatus)
            query = query.Where(d => d.Estatus == estatus);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = $"%{request.Q.Trim()}%";
            query = query.Where(d =>
                EF.Functions.ILike(d.Clave, q) || EF.Functions.ILike(d.Nombre, q));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await (
            from d in query
            join g in _db.GruposDim2.AsNoTracking() on d.GrupoDim2Id equals g.Id
            orderby d.Clave
            select new Dim2ListItem(
                d.Id, d.Dim1Id, d.Clave, d.Nombre,
                d.GrupoDim2Id, g.Nombre, d.Estatus, d.Version))
            .Skip(request.Offset)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Dim2ListItem>(items, total, request.Offset, request.Limit);
    }
}

// ─── Dim3 ────────────────────────────────────────────────────────────────────

public sealed record Dim3ListItem(
    Guid Id,
    Guid Dim2Id,
    string Clave,
    string Nombre,
    Guid GrupoDim3Id,
    string GrupoDim3Nombre,
    string Dim2Clave,
    string Dim2Nombre,
    EstatusCatalogo Estatus,
    int Version);

public sealed record ListarDim3Query(
    Guid? Dim2Id,
    Guid? GrupoDim3Id,
    EstatusCatalogo? Estatus,
    string? Q,
    int Offset,
    int Limit) : IRequest<PagedResponse<Dim3ListItem>>;

public sealed class ListarDim3Handler
    : IRequestHandler<ListarDim3Query, PagedResponse<Dim3ListItem>>
{
    private readonly CentrosCostoDbContext _db;

    public ListarDim3Handler(CentrosCostoDbContext db) => _db = db;

    public async Task<PagedResponse<Dim3ListItem>> Handle(
        ListarDim3Query request, CancellationToken cancellationToken)
    {
        var query = _db.Dim3s.AsNoTracking().AsQueryable();
        if (request.Dim2Id is Guid dim2Id)
            query = query.Where(e => e.Dim2Id == dim2Id);
        if (request.GrupoDim3Id is Guid grupoId)
            query = query.Where(e => e.GrupoDim3Id == grupoId);
        if (request.Estatus is EstatusCatalogo estatus)
            query = query.Where(e => e.Estatus == estatus);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = $"%{request.Q.Trim()}%";
            query = query.Where(e =>
                EF.Functions.ILike(e.Clave, q) || EF.Functions.ILike(e.Nombre, q));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await (
            from e in query
            join s in _db.GruposDim3.AsNoTracking() on e.GrupoDim3Id equals s.Id
            join d in _db.Dim2s.AsNoTracking() on e.Dim2Id equals d.Id
            orderby e.Clave
            select new Dim3ListItem(
                e.Id, e.Dim2Id, e.Clave, e.Nombre,
                e.GrupoDim3Id, s.Nombre, d.Clave, d.Nombre, e.Estatus, e.Version))
            .Skip(request.Offset)
            .Take(request.Limit)
            .ToListAsync(cancellationToken);

        return new PagedResponse<Dim3ListItem>(items, total, request.Offset, request.Limit);
    }
}
