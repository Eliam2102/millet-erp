using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Almacen.Application.Reorden;

// ============================================================================
// Queries de la configuración de reorden N1/N2 (ADR-0047 PR5.A). Read-only
// directo sobre almacen.configuraciones_reorden; enriquece clave/descripción del
// artículo en batch (ADR-0042), igual que ListarAsignaciones. El nombre de la
// entidad (sucursal/almacén) se resuelve en el FE (5.F) — aquí va el id crudo.
// ============================================================================

public sealed record ListarConfiguracionesReordenQuery(
    Guid? ArticuloId,
    NivelReorden? Nivel,
    Guid? EntidadId,
    EstatusCatalogo? Estatus,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<ConfiguracionReordenListItem>>;

public sealed record ConfiguracionReordenListItem(
    Guid Id,
    Guid ArticuloId,
    NivelReorden Nivel,
    Guid EntidadId,
    decimal Minimo,
    decimal Maximo,
    decimal PuntoReorden,
    bool AutoRequisicion,
    ObjetivoReposicion Objetivo,
    EstatusCatalogo Estatus,
    string? ArticuloClave,
    string? ArticuloDescripcion);

public sealed class ListarConfiguracionesReordenHandler
    : IRequestHandler<ListarConfiguracionesReordenQuery, AlmacenPagedResponse<ConfiguracionReordenListItem>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ListarConfiguracionesReordenHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<AlmacenPagedResponse<ConfiguracionReordenListItem>> Handle(
        ListarConfiguracionesReordenQuery request, CancellationToken cancellationToken)
    {
        var query = _db.ConfiguracionesReorden.AsNoTracking().AsQueryable();
        if (request.ArticuloId is Guid aid) query = query.Where(c => c.ArticuloId == aid);
        if (request.Nivel is NivelReorden niv) query = query.Where(c => c.Nivel == niv);
        if (request.EntidadId is Guid eid) query = query.Where(c => c.EntidadId == eid);
        if (request.Estatus is EstatusCatalogo est) query = query.Where(c => c.Estatus == est);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(c => c.ArticuloId).ThenBy(c => c.Nivel).ThenBy(c => c.EntidadId)
            .Skip(request.Offset).Take(request.Limit)
            .Select(c => new ConfiguracionReordenListItem(
                c.Id, c.ArticuloId, c.Nivel, c.EntidadId,
                c.Minimo, c.Maximo, c.PuntoReorden,
                c.AutoRequisicion, c.Objetivo, c.Estatus,
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

        return new AlmacenPagedResponse<ConfiguracionReordenListItem>(
            enriquecidos, request.Offset, request.Limit, total);
    }
}

public sealed record ObtenerConfiguracionReordenPorIdQuery(Guid Id)
    : IRequest<ConfiguracionReordenListItem?>;

public sealed class ObtenerConfiguracionReordenPorIdHandler
    : IRequestHandler<ObtenerConfiguracionReordenPorIdQuery, ConfiguracionReordenListItem?>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ObtenerConfiguracionReordenPorIdHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<ConfiguracionReordenListItem?> Handle(
        ObtenerConfiguracionReordenPorIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _db.ConfiguracionesReorden.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (c is null) return null;

        var art = await _articulos.ObtenerAsync(c.ArticuloId, cancellationToken);
        return new ConfiguracionReordenListItem(
            c.Id, c.ArticuloId, c.Nivel, c.EntidadId,
            c.Minimo, c.Maximo, c.PuntoReorden,
            c.AutoRequisicion, c.Objetivo, c.Estatus,
            art?.Clave, art?.Descripcion);
    }
}
