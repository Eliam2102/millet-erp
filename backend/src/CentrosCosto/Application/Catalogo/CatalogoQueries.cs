using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CentrosCosto.Application.Catalogo;

// ============================================================================
// Queries por id del catálogo (CECO-PR4). Alimentan el ETag del flujo
// If-Match (devuelven Version) y resuelven grupos/padres server-side
// (ADR-0042) — TODO local al esquema: con la separación total ya no hay
// read-ports que consultar.
// ============================================================================

// ─── Dim1 ────────────────────────────────────────────────────────────────────

public sealed record ObtenerDim1PorIdQuery(Guid Id) : IRequest<Dim1Response>;

public sealed class ObtenerDim1PorIdHandler : IRequestHandler<ObtenerDim1PorIdQuery, Dim1Response>
{
    private readonly CentrosCostoDbContext _db;

    public ObtenerDim1PorIdHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim1Response> Handle(ObtenerDim1PorIdQuery request, CancellationToken cancellationToken)
    {
        var dim1 = await _db.Dim1s.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_DIM1_NO_ENCONTRADA", $"No existe Dimensión 1 con id '{request.Id}'.");

        return new Dim1Response(dim1.Id, dim1.Clave, dim1.Nombre, dim1.Estatus, dim1.Version);
    }
}

// ─── Grupos ──────────────────────────────────────────────────────────────────

public sealed record ObtenerGrupoDim2PorIdQuery(Guid Id) : IRequest<GrupoDimResponse>;

public sealed class ObtenerGrupoDim2PorIdHandler : IRequestHandler<ObtenerGrupoDim2PorIdQuery, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public ObtenerGrupoDim2PorIdHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(
        ObtenerGrupoDim2PorIdQuery request, CancellationToken cancellationToken)
    {
        var grupo = await _db.GruposDim2.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_GRUPO_DIM2_NO_ENCONTRADO", $"No existe grupo de dimensión 2 con id '{request.Id}'.");

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

public sealed record ObtenerGrupoDim3PorIdQuery(Guid Id) : IRequest<GrupoDimResponse>;

public sealed class ObtenerGrupoDim3PorIdHandler : IRequestHandler<ObtenerGrupoDim3PorIdQuery, GrupoDimResponse>
{
    private readonly CentrosCostoDbContext _db;

    public ObtenerGrupoDim3PorIdHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<GrupoDimResponse> Handle(
        ObtenerGrupoDim3PorIdQuery request, CancellationToken cancellationToken)
    {
        var grupo = await _db.GruposDim3.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "CECO_GRUPO_DIM3_NO_ENCONTRADO", $"No existe grupo de dimensión 3 con id '{request.Id}'.");

        return new GrupoDimResponse(grupo.Id, grupo.Nombre, grupo.Estatus, grupo.Version);
    }
}

// ─── Dim2 (con el nombre del grupo resuelto) ─────────────────────────────────

public sealed record Dim2DetalleDto(
    Guid Id,
    Guid Dim1Id,
    string Clave,
    string Nombre,
    Guid GrupoDim2Id,
    string GrupoDim2Nombre,
    EstatusCatalogo Estatus,
    int Version);

public sealed record ObtenerDim2PorIdQuery(Guid Id) : IRequest<Dim2DetalleDto>;

public sealed class ObtenerDim2PorIdHandler : IRequestHandler<ObtenerDim2PorIdQuery, Dim2DetalleDto>
{
    private readonly CentrosCostoDbContext _db;

    public ObtenerDim2PorIdHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim2DetalleDto> Handle(
        ObtenerDim2PorIdQuery request, CancellationToken cancellationToken)
    {
        var dto = await (
            from d in _db.Dim2s.AsNoTracking()
            join g in _db.GruposDim2.AsNoTracking() on d.GrupoDim2Id equals g.Id
            where d.Id == request.Id
            select new Dim2DetalleDto(
                d.Id, d.Dim1Id, d.Clave, d.Nombre,
                d.GrupoDim2Id, g.Nombre, d.Estatus, d.Version))
            .FirstOrDefaultAsync(cancellationToken);

        return dto ?? throw new EntityNotFoundException(
            "CECO_DIM2_NO_ENCONTRADA", $"No existe Dimensión 2 con id '{request.Id}'.");
    }
}

// ─── Dim3 (con grupo y contexto de la Dim2 resueltos) ────────────────────────

public sealed record Dim3DetalleDto(
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

public sealed record ObtenerDim3PorIdQuery(Guid Id) : IRequest<Dim3DetalleDto>;

public sealed class ObtenerDim3PorIdHandler : IRequestHandler<ObtenerDim3PorIdQuery, Dim3DetalleDto>
{
    private readonly CentrosCostoDbContext _db;

    public ObtenerDim3PorIdHandler(CentrosCostoDbContext db) => _db = db;

    public async Task<Dim3DetalleDto> Handle(
        ObtenerDim3PorIdQuery request, CancellationToken cancellationToken)
    {
        var dto = await (
            from e in _db.Dim3s.AsNoTracking()
            join s in _db.GruposDim3.AsNoTracking() on e.GrupoDim3Id equals s.Id
            join d in _db.Dim2s.AsNoTracking() on e.Dim2Id equals d.Id
            where e.Id == request.Id
            select new Dim3DetalleDto(
                e.Id, e.Dim2Id, e.Clave, e.Nombre,
                e.GrupoDim3Id, s.Nombre, d.Clave, d.Nombre, e.Estatus, e.Version))
            .FirstOrDefaultAsync(cancellationToken);

        return dto ?? throw new EntityNotFoundException(
            "CECO_DIM3_NO_ENCONTRADA", $"No existe Dimensión 3 con id '{request.Id}'.");
    }
}
