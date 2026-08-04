using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Conteos;

// ============================================================================
// Queries del conteo (F7-PR1).
//
// CRÍTICO — Captura sin sesgo (A6):
// - `LineaConteoParaCapturarDto` NO incluye `cantidad_teorica`.
// - `LineaConteoComparacionDto` SÍ la incluye, solo para el endpoint del
//   aprobador con permiso distinto (almacen.inventarios.aprobar-nivel1+).
// ============================================================================

public sealed record ConteoListItem(
    Guid Id,
    TipoConteo Tipo,
    EstadoConteo Estado,
    DateOnly FechaPlanificada,
    Guid? SubAlmacenId,
    string? FiltroFamilia,
    DateTimeOffset? FechaInicio,
    DateTimeOffset? FechaCierre,
    int CantidadLineas,
    int CantidadCapturadas);

public sealed record ConteoDetalle(
    Guid Id,
    TipoConteo Tipo,
    EstadoConteo Estado,
    DateOnly FechaPlanificada,
    Guid? SubAlmacenId,
    string? FiltroFamilia,
    Guid ResponsableId,
    DateTimeOffset? FechaInicio,
    DateTimeOffset? SnapshotCapturadoAt,
    Guid? AprobadorId,
    DateTimeOffset? FechaAprobacion,
    int Version,
    int CantidadLineas,
    int CantidadCapturadas,
    // Etiquetas legibles (ADR-0042): el detalle muestra clave/nombre, nunca
    // el GUID. Con default null las construcciones previas siguen compilando;
    // el fallback del FE es el id.
    string? SubAlmacenClave = null,
    string? ResponsableNombre = null,
    string? AprobadorNombre = null);

/// <summary>
/// DTO del endpoint de captura. <b>NO incluye cantidad_teorica</b>
/// — el contador captura sin sesgo (A6).
/// </summary>
public sealed record LineaConteoParaCapturarDto(
    Guid Id,
    Guid ArticuloId,
    Guid SubAlmacenId,
    // C7.2c: el conteo es por rack. UbicacionClave enriquecida por JOIN (nunca
    // el GUID en pantalla).
    Guid UbicacionId,
    string UbicacionClave,
    decimal? CantidadRealCapturada,
    bool RequiereRecuento,
    // Artículo y sub-almacén legibles: clave de sub-almacén por JOIN local;
    // clave/descripción de artículo vía IArticuloReadPort en batch (ADR-0042).
    // Nota A6: la descripción del artículo no revela cantidad teórica.
    string? ArticuloClave = null,
    string? ArticuloDescripcion = null,
    string? SubAlmacenClave = null);

/// <summary>
/// DTO del endpoint de comparación. SÍ incluye cantidad_teorica +
/// diferencias + valor. Endpoint separado, permiso distinto.
/// </summary>
public sealed record LineaConteoComparacionDto(
    Guid Id,
    Guid ArticuloId,
    Guid SubAlmacenId,
    // C7.2c: rack contado. Clave enriquecida por JOIN.
    Guid UbicacionId,
    string UbicacionClave,
    decimal CantidadTeorica,
    decimal CostoPromedioSnapshot,
    decimal? CantidadRealCapturada,
    decimal? VariacionAbsoluta,
    decimal? VariacionPorcentaje,
    decimal? VariacionValorMxn,
    bool RequiereRecuento,
    bool AprobadoIndividualmente,
    string? Justificacion,
    // Artículo y sub-almacén legibles, mismo enriquecimiento que la captura.
    string? ArticuloClave = null,
    string? ArticuloDescripcion = null,
    string? SubAlmacenClave = null);

public sealed record ListarConteosQuery(
    EstadoConteo? Estado,
    TipoConteo? Tipo,
    Guid? SubAlmacenId,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<ConteoListItem>>;

public sealed class ListarConteosHandler
    : IRequestHandler<ListarConteosQuery, AlmacenPagedResponse<ConteoListItem>>
{
    private readonly AlmacenDbContext _db;
    public ListarConteosHandler(AlmacenDbContext db) => _db = db;

    public async Task<AlmacenPagedResponse<ConteoListItem>> Handle(
        ListarConteosQuery request, CancellationToken cancellationToken)
    {
        IQueryable<ConteoInventario> query = _db.Set<ConteoInventario>().AsNoTracking();
        if (request.Estado is EstadoConteo e) query = query.Where(c => c.Estado == e);
        if (request.Tipo is TipoConteo t) query = query.Where(c => c.Tipo == t);
        if (request.SubAlmacenId is Guid sid) query = query.Where(c => c.SubAlmacenId == sid);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.FechaPlanificada)
            .Skip(request.Offset).Take(request.Limit)
            .Select(c => new ConteoListItem(
                c.Id, c.Tipo, c.Estado, c.FechaPlanificada,
                c.SubAlmacenId, c.FiltroFamilia,
                c.FechaInicio, c.FechaCierre,
                c.Lineas.Count,
                c.Lineas.Count(l => l.CantidadRealCapturada != null)))
            .ToListAsync(cancellationToken);

        return new AlmacenPagedResponse<ConteoListItem>(items, request.Offset, request.Limit, total);
    }
}

public sealed record ObtenerConteoPorIdQuery(Guid Id) : IRequest<ConteoDetalle?>;

public sealed class ObtenerConteoPorIdHandler : IRequestHandler<ObtenerConteoPorIdQuery, ConteoDetalle?>
{
    private readonly AlmacenDbContext _db;
    private readonly IUsuarioReadPort _usuarios;

    public ObtenerConteoPorIdHandler(AlmacenDbContext db, IUsuarioReadPort usuarios)
    {
        _db = db;
        _usuarios = usuarios;
    }

    public async Task<ConteoDetalle?> Handle(ObtenerConteoPorIdQuery request, CancellationToken cancellationToken)
    {
        var c = await _db.Set<ConteoInventario>().AsNoTracking()
            .Include(x => x.Lineas)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (c is null) return null;

        var subAlmacenClave = c.SubAlmacenId is Guid sid
            ? await _db.SubAlmacenes.AsNoTracking()
                .Where(s => s.Id == sid)
                .Select(s => s.Clave)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var usuarioIds = new List<Guid> { c.ResponsableId };
        if (c.AprobadorId is Guid aprobadorId) usuarioIds.Add(aprobadorId);
        var nombres = await _usuarios.ObtenerNombresAsync(
            usuarioIds.Distinct().ToArray(), cancellationToken);

        return new ConteoDetalle(
            Id: c.Id, Tipo: c.Tipo, Estado: c.Estado,
            FechaPlanificada: c.FechaPlanificada,
            SubAlmacenId: c.SubAlmacenId, FiltroFamilia: c.FiltroFamilia,
            ResponsableId: c.ResponsableId,
            FechaInicio: c.FechaInicio, SnapshotCapturadoAt: c.SnapshotCapturadoAt,
            AprobadorId: c.AprobadorId, FechaAprobacion: c.FechaAprobacion,
            Version: c.Version,
            CantidadLineas: c.Lineas.Count,
            CantidadCapturadas: c.Lineas.Count(l => l.CantidadRealCapturada != null),
            SubAlmacenClave: subAlmacenClave,
            ResponsableNombre: nombres.TryGetValue(c.ResponsableId, out var responsable)
                ? responsable
                : null,
            AprobadorNombre: c.AprobadorId is Guid ap && nombres.TryGetValue(ap, out var aprobador)
                ? aprobador
                : null);
    }
}

// ─── Captura sin sesgo — DTO específico ──────────────────────────────────────

public sealed record ListarLineasParaCapturarQuery(
    Guid ConteoId) : IRequest<IReadOnlyList<LineaConteoParaCapturarDto>>;

public sealed class ListarLineasParaCapturarHandler
    : IRequestHandler<ListarLineasParaCapturarQuery, IReadOnlyList<LineaConteoParaCapturarDto>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ListarLineasParaCapturarHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<IReadOnlyList<LineaConteoParaCapturarDto>> Handle(
        ListarLineasParaCapturarQuery request, CancellationToken cancellationToken)
    {
        var lineas = await (
            from l in _db.Set<LineaConteo>().AsNoTracking()
            join u in _db.Ubicaciones.AsNoTracking() on l.UbicacionId equals u.Id
            join s in _db.SubAlmacenes.AsNoTracking() on l.SubAlmacenId equals s.Id
            where l.ConteoId == request.ConteoId
            select new LineaConteoParaCapturarDto(
                l.Id, l.ArticuloId, l.SubAlmacenId,
                l.UbicacionId, u.Clave,
                l.CantidadRealCapturada, l.RequiereRecuento,
                null, null, s.Clave))
            .ToListAsync(cancellationToken);

        return await EnriquecerYOrdenarConteoLineas.PorArticuloAsync(
            lineas, _articulos,
            l => l.ArticuloId,
            (l, art) => l with { ArticuloClave = art.Clave, ArticuloDescripcion = art.Descripcion },
            l => (l.ArticuloClave, l.ArticuloId, l.UbicacionClave),
            cancellationToken);
    }
}

// ─── Comparación (solo aprobador) ────────────────────────────────────────────

public sealed record ListarLineasComparacionQuery(
    Guid ConteoId) : IRequest<IReadOnlyList<LineaConteoComparacionDto>>;

public sealed class ListarLineasComparacionHandler
    : IRequestHandler<ListarLineasComparacionQuery, IReadOnlyList<LineaConteoComparacionDto>>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;

    public ListarLineasComparacionHandler(AlmacenDbContext db, IArticuloReadPort articulos)
    {
        _db = db;
        _articulos = articulos;
    }

    public async Task<IReadOnlyList<LineaConteoComparacionDto>> Handle(
        ListarLineasComparacionQuery request, CancellationToken cancellationToken)
    {
        var filas = await (
            from l in _db.Set<LineaConteo>().AsNoTracking()
            join u in _db.Ubicaciones.AsNoTracking() on l.UbicacionId equals u.Id
            join s in _db.SubAlmacenes.AsNoTracking() on l.SubAlmacenId equals s.Id
            where l.ConteoId == request.ConteoId
            select new { Linea = l, UbicacionClave = u.Clave, SubAlmacenClave = s.Clave })
            .ToListAsync(cancellationToken);

        var lineas = filas.Select(x =>
        {
            var l = x.Linea;
            decimal? variacionAbs = null, variacionPct = null, variacionVal = null;
            if (l.CantidadRealCapturada is decimal real)
            {
                var diff = real - l.CantidadTeorica;
                variacionAbs = diff;
                variacionPct = l.CantidadTeorica == 0
                    ? (decimal?)null
                    : Math.Round(diff / l.CantidadTeorica * 100m, 2);
                variacionVal = Math.Round(diff * l.CostoPromedioSnapshot, 2);
            }
            return new LineaConteoComparacionDto(
                l.Id, l.ArticuloId, l.SubAlmacenId,
                l.UbicacionId, x.UbicacionClave,
                l.CantidadTeorica, l.CostoPromedioSnapshot,
                l.CantidadRealCapturada,
                variacionAbs, variacionPct, variacionVal,
                l.RequiereRecuento, l.AprobadoIndividualmente, l.Justificacion,
                null, null, x.SubAlmacenClave);
        }).ToList();

        return await EnriquecerYOrdenarConteoLineas.PorArticuloAsync(
            lineas, _articulos,
            l => l.ArticuloId,
            (l, art) => l with { ArticuloClave = art.Clave, ArticuloDescripcion = art.Descripcion },
            l => (l.ArticuloClave, l.ArticuloId, l.UbicacionClave),
            cancellationToken);
    }
}

/// <summary>
/// Enriquecimiento compartido de las líneas de conteo: resuelve
/// <c>articuloId → clave/descripción</c> en <b>batch</b> (una sola llamada al
/// puerto por request, ADR-0042) y ordena por clave de artículo y rack para
/// que la lista sea recorrible en piso. Las claves que el puerto no resuelve
/// quedan null (el FE cae al id truncado).
/// </summary>
internal static class EnriquecerYOrdenarConteoLineas
{
    public static async Task<IReadOnlyList<T>> PorArticuloAsync<T>(
        IReadOnlyList<T> lineas,
        IArticuloReadPort articulos,
        Func<T, Guid> articuloId,
        Func<T, ArticuloLectura, T> enriquecer,
        Func<T, (string? ArticuloClave, Guid ArticuloId, string UbicacionClave)> ordenarPor,
        CancellationToken cancellationToken)
    {
        var ids = lineas.Select(articuloId).Distinct().ToArray();
        var resueltos = ids.Length > 0
            ? await articulos.ObtenerPorIdsAsync(ids, cancellationToken)
            : new Dictionary<Guid, ArticuloLectura>();

        return lineas
            .Select(l => resueltos.TryGetValue(articuloId(l), out var art)
                ? enriquecer(l, art)
                : l)
            .OrderBy(l => ordenarPor(l).ArticuloClave ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(l => ordenarPor(l).ArticuloId)
            .ThenBy(l => ordenarPor(l).UbicacionClave, StringComparer.Ordinal)
            .ToList();
    }
}
