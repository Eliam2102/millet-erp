using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Almacen.Application.Saldos;

// ============================================================================
// Queries de saldos para el FE del módulo Almacén (F2-PR2). Read-only
// directo sobre `almacen.saldos_inventario`. Para consumidores
// cross-módulo, usar `IAlmacenSaldoQueryPort` (open host service).
// ============================================================================

public sealed record SaldoListItem(
    Guid SubAlmacenId,
    Guid ArticuloId,
    decimal Cantidad,
    decimal CantidadDisponible,
    decimal CostoPromedioMxn,
    decimal ValorInventarioMxn,
    // Resueltos en backend (ADR-0042). Null = el read-port no resolvió; el
    // frontend cae al ArticuloId crudo.
    string? ArticuloClave,
    string? ArticuloDescripcion);

public sealed record ListarSaldosQuery(
    Guid? SubAlmacenId,
    Guid? ArticuloId,
    bool SoloConStock,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<SaldoListItem>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.almacenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class ListarSaldosHandler
    : IRequestHandler<ListarSaldosQuery, AlmacenPagedResponse<SaldoListItem>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ListarSaldosHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    /// <summary>
    /// Filtros del listado y de los totales (mismo criterio). El alcance de sucursal
    /// se aplica junto con los filtros, antes de agrupar o sumar (principio 7).
    /// </summary>
    internal static IQueryable<Millet.Almacen.Domain.Saldos.SaldoInventario> SaldosFiltrados(
        AlmacenDbContext db, Guid? subAlmacenId, Guid? articuloId, bool soloConStock,
        IReadOnlyList<Guid>? sucursalesPermitidas)
    {
        var query = db.SaldosInventario.AsNoTracking().AsQueryable();
        if (subAlmacenId is Guid sid) query = query.Where(s => s.SubAlmacenId == sid);
        if (articuloId is Guid aid) query = query.Where(s => s.ArticuloId == aid);
        if (soloConStock) query = query.Where(s => s.Cantidad > 0);
        if (sucursalesPermitidas is { } suc)
            query = query.Where(s => AlcanceSucursalAlmacen.SubAlmacenesPermitidos(db, suc).Contains(s.SubAlmacenId));
        return query;
    }

    public async Task<AlmacenPagedResponse<SaldoListItem>> Handle(
        ListarSaldosQuery request, CancellationToken cancellationToken)
    {
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(_db, request.SubAlmacenId,
            request.SucursalesPermitidas, cancellationToken);
        var query = SaldosFiltrados(_db, request.SubAlmacenId, request.ArticuloId,
            request.SoloConStock, request.SucursalesPermitidas);

        // C7.2a: agregado por (sub-almacén, artículo) — con N bins habría
        // filas duplicadas indistinguibles (el listado no expone ubicación).
        // Puente determinista: SUM + costo ponderado. El grano por-ubicación
        // (columna de bin) se difiere a C7.2b con su decisión de UX.
        var agrupado = query
            .GroupBy(s => new { s.SubAlmacenId, s.ArticuloId })
            .Select(g => new
            {
                g.Key.SubAlmacenId,
                g.Key.ArticuloId,
                Cantidad = g.Sum(s => s.Cantidad),
                CantidadDisponible = g.Sum(s => s.CantidadDisponible),
                ValorInventarioMxn = g.Sum(s => s.ValorInventarioMxn),
            });

        var total = await agrupado.CountAsync(cancellationToken);
        var pagina = await agrupado
            .OrderBy(x => x.SubAlmacenId).ThenBy(x => x.ArticuloId)
            .Skip(request.Offset).Take(request.Limit)
            .ToListAsync(cancellationToken);
        var items = pagina
            .Select(x => new SaldoListItem(
                x.SubAlmacenId, x.ArticuloId,
                x.Cantidad, x.CantidadDisponible,
                x.Cantidad > 0 ? Math.Round(x.ValorInventarioMxn / x.Cantidad, 4) : 0m,
                x.ValorInventarioMxn,
                null, null)) // ArticuloClave/Descripcion: se enriquecen en batch abajo (ADR-0042).
            .ToList();

        // Nombre del artículo: batch sobre los ArticuloId distintos de la página
        // (anti-N+1, state-agnostic). Fallback al id cuando el read-port no
        // resuelve (los dos campos quedan null y el frontend cae al id).
        var articuloIds = items.Select(i => i.ArticuloId).Distinct().ToArray();
        var articulos = articuloIds.Length > 0
            ? await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken)
            : new Dictionary<Guid, ArticuloLectura>();

        // ALM-04: el material en revisión (MAT-REV) existe físicamente pero no es surtible: disponible = 0.
        var subIds = items.Select(i => i.SubAlmacenId).Distinct().ToArray();
        var subsEnRevision = (await _db.SubAlmacenes.AsNoTracking()
            .Where(sa => subIds.Contains(sa.Id) && sa.Tipo == TipoSubAlmacen.MaterialEnRevision)
            .Select(sa => sa.Id).ToListAsync(cancellationToken)).ToHashSet();

        var enriquecidos = items
            .Select(i => i with { CantidadDisponible = subsEnRevision.Contains(i.SubAlmacenId) ? 0m : i.CantidadDisponible })
            .Select(i => articulos.TryGetValue(i.ArticuloId, out var art)
                ? i with { ArticuloClave = art.Clave, ArticuloDescripcion = art.Descripcion }
                : i)
            .ToList();

        return new AlmacenPagedResponse<SaldoListItem>(
            enriquecidos, request.Offset, request.Limit, total);
    }
}

// ─── Totales de saldos (UX-F2-ALM-05) ────────────────────────────────────────
// Mismos filtros y alcance que ListarSaldos, sobre TODO el conjunto filtrado (no
// la página). El valor en MXN siempre es sumable; la cantidad solo cuando todos
// los artículos del conjunto comparten una única unidad de medida (sumar piezas
// con metros no tiene sentido).

public sealed record SaldosTotalesResponse(
    decimal ValorInventarioMxn,
    decimal? CantidadTotal,
    string? UnidadMedida,
    int Articulos);

public sealed record ObtenerTotalesSaldosQuery(
    Guid? SubAlmacenId,
    Guid? ArticuloId,
    bool SoloConStock) : IRequest<SaldosTotalesResponse>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.almacenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class ObtenerTotalesSaldosHandler
    : IRequestHandler<ObtenerTotalesSaldosQuery, SaldosTotalesResponse>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ObtenerTotalesSaldosHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<SaldosTotalesResponse> Handle(
        ObtenerTotalesSaldosQuery request, CancellationToken cancellationToken)
    {
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(_db, request.SubAlmacenId,
            request.SucursalesPermitidas, cancellationToken);
        var query = ListarSaldosHandler.SaldosFiltrados(_db, request.SubAlmacenId,
            request.ArticuloId, request.SoloConStock, request.SucursalesPermitidas);

        var porArticulo = await query
            .GroupBy(s => s.ArticuloId)
            .Select(g => new
            {
                ArticuloId = g.Key,
                Cantidad = g.Sum(s => s.Cantidad),
                Valor = g.Sum(s => s.ValorInventarioMxn),
            })
            .ToListAsync(cancellationToken);

        var valor = porArticulo.Sum(x => x.Valor);
        if (porArticulo.Count == 0)
            return new SaldosTotalesResponse(0m, null, null, 0);

        var articulos = await _articulos.ObtenerPorIdsAsync(
            porArticulo.Select(x => x.ArticuloId).ToArray(), cancellationToken);
        // Un artículo sin resolver cuenta como unidad desconocida: no se suma cantidad.
        var unidades = porArticulo
            .Select(x => articulos.TryGetValue(x.ArticuloId, out var a) ? a.UnidadMedida : null)
            .Distinct()
            .ToList();
        var unica = unidades.Count == 1 ? unidades[0] : null;

        return new SaldosTotalesResponse(
            valor,
            unica is null ? null : porArticulo.Sum(x => x.Cantidad),
            unica,
            porArticulo.Count);
    }
}

// ─── Saldo por ubicación de un artículo (C7.2b) ──────────────────────────────
// Puebla el selector de bin en SALIDAS: las ubicaciones que tienen existencia
// (>0) del artículo, incluida la ÚNICA (es_default) para agotar el histórico.
// Salida-por-línea C2: el sub es opcional — sin él, lista los bins de todos los
// subs (la salida-con-RQ deriva el sub del bin). Grano por-ubicación (a
// diferencia de ListarSaldos, que agrega por sub). No paginado — pocos bins.

public sealed record SaldoUbicacionItem(
    Guid UbicacionId,
    string Clave,
    string Nombre,
    // Salida-por-línea C1: claves de los padres N3/N2 para pintar la ruta
    // "ALM › SUB · UBI" en el selector de salida. Join in-context INNER (toda
    // ubicación tiene sub y todo sub tiene almacén) → siempre no-null, sin
    // cambio de row-set. Mismo patrón que UbicacionListItem. N1 (sucursal) diferido.
    string SubAlmacenClave,
    string AlmacenClave,
    bool EsDefault,
    decimal Cantidad,
    decimal CantidadDisponible,
    decimal CostoPromedioMxn);

public sealed record SaldosPorUbicacionQuery(
    Guid ArticuloId,
    // Salida-por-línea C2: opcional. null = todos los bins con existencia del
    // artículo en cualquier sub (la salida-con-RQ ya no tiene sub de cabecera;
    // el pick cruzado se rechaza en submit vía SALIDA_MULTI_SUBALMACEN). Con
    // valor = acotado a ese sub (vale, devolución a proveedor).
    Guid? SubAlmacenId) : IRequest<IReadOnlyList<SaldoUbicacionItem>>, ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "almacen.almacenes.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class SaldosPorUbicacionHandler
    : IRequestHandler<SaldosPorUbicacionQuery, IReadOnlyList<SaldoUbicacionItem>>
{
    private readonly AlmacenDbContext _db;

    public SaldosPorUbicacionHandler(AlmacenDbContext db) => _db = db;

    public async Task<IReadOnlyList<SaldoUbicacionItem>> Handle(
        SaldosPorUbicacionQuery request, CancellationToken cancellationToken)
    {
        await AlcanceSucursalAlmacen.VerificarSubAlmacenAsync(_db, request.SubAlmacenId,
            request.SucursalesPermitidas, cancellationToken);
        // Materializado antes del join: evita pasar una lista nula a EF dentro del where.
        var subsPermitidos = request.SucursalesPermitidas is { } suc
            ? await AlcanceSucursalAlmacen.SubAlmacenesPermitidos(_db, suc).ToListAsync(cancellationToken)
            : null;
        return await (
            from s in _db.SaldosInventario.AsNoTracking()
            join u in _db.Ubicaciones.AsNoTracking() on s.UbicacionId equals u.Id
            join sa in _db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            join al in _db.Almacenes.AsNoTracking() on sa.AlmacenId equals al.Id
            where s.ArticuloId == request.ArticuloId
               && (request.SubAlmacenId == null || s.SubAlmacenId == request.SubAlmacenId)
               && s.Cantidad > 0
               // ALM-04: sin sub explícito (salida con RQ) no se ofrece material en revisión.
               && (request.SubAlmacenId != null || sa.Tipo != TipoSubAlmacen.MaterialEnRevision)
               && (subsPermitidos == null || subsPermitidos.Contains(s.SubAlmacenId))
            orderby u.EsDefault, u.Clave
            select new SaldoUbicacionItem(
                u.Id, u.Clave, u.Nombre, sa.Clave, al.Clave, u.EsDefault,
                s.Cantidad, sa.Tipo == TipoSubAlmacen.MaterialEnRevision ? 0m : s.CantidadDisponible, s.CostoPromedioMxn))
            .ToListAsync(cancellationToken);
    }
}
