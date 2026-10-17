using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Application.Revision;

// ============================================================================
// ALM-04 (G1.3): bandeja «Material en revisión». Una fila por línea de un
// movimiento EntradaCompra registrado cuyo bin está en un sub-almacén MAT-REV
// (el hermano que crea la recepción con daño).
// ============================================================================

public sealed record MaterialEnRevisionItem(
    Guid LineaRevisionId, Guid MovimientoRevisionId, Guid RecepcionId, string RecepcionFolio, DateOnly FechaRecepcion,
    Guid? OrdenCompraId, string? OrdenCompraFolio, Guid ArticuloId, string? ArticuloClave, string? ArticuloDescripcion,
    string UnidadMedida, decimal CantidadEnRevision, decimal CantidadDictaminada, decimal CantidadEnDevolucion,
    decimal CantidadPendiente, string? MotivoDano, Guid UbicacionRevisionId, Guid SubAlmacenRevisionId,
    Guid? UbicacionDisponibleSugeridaId, DateTimeOffset? FechaLimite, bool Vencida,
    IReadOnlyList<DictamenRevisionItem> Dictamenes);

public sealed record DictamenRevisionItem(
    Guid Id, ResultadoDictamen Resultado, decimal Cantidad, string Motivo, Guid DictaminadoPor,
    DateTimeOffset DictaminadoAt, Guid? DevolucionAProveedorId);

public sealed record ListarMaterialEnRevisionQuery(bool SoloPendientes = true, string? Q = null)
    : IRequest<IReadOnlyList<MaterialEnRevisionItem>>, IDocumentoScopedQuery
{
    public string TipoDocumento => "recepcion";
    public string PermisoTodasSucursales => "almacen.entradas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed record LineaRevisionInfo(
    Guid LineaRevisionId, Guid MovimientoRevisionId, Guid EmpresaId, Guid SucursalId, Guid SubAlmacenRevisionId,
    Guid UbicacionRevisionId, Guid ArticuloId, string UnidadMedida, decimal Cantidad, decimal CostoUnitarioMxn,
    Guid? LineaOcId, Guid? OrdenCompraId, string? MotivoDano, string Etiqueta);

public sealed record ObtenerLineaRevisionQuery(Guid LineaRevisionId) : IRequest<LineaRevisionInfo?>;

public sealed class ListarMaterialEnRevisionHandler(
    AlmacenDbContext db, IArticuloReadPort articulos, IComprasOcReadPort ordenesCompra, ICalendarioHabil calendario)
    : IRequestHandler<ListarMaterialEnRevisionQuery, IReadOnlyList<MaterialEnRevisionItem>>
{
    // ponytail: V45 (5 días hábiles = 120 h hábiles), sin ratificar por Millet; parametrizar en AlmacenSettings cuando se valide.
    internal const int PlazoDictamenHorasHabiles = 120;

    public async Task<IReadOnlyList<MaterialEnRevisionItem>> Handle(
        ListarMaterialEnRevisionQuery request, CancellationToken cancellationToken)
    {
        var permitidos = request.DocumentosPermitidos;
        var origen = await (
            from l in db.LineasMovimiento.AsNoTracking()
            join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            join u in db.Ubicaciones.AsNoTracking() on l.UbicacionId equals (Guid?)u.Id
            join sa in db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            where m.Tipo == TipoMovimiento.EntradaCompra
                && m.Estado == EstadoMovimiento.Registrado
                && sa.Tipo == TipoSubAlmacen.MaterialEnRevision
                && (permitidos == null || permitidos.Contains(m.Id))
            select new
            {
                Linea = l,
                MovimientoId = m.Id,
                m.RecepcionOrigenId,
                m.OcId,
                m.FechaMovimiento,
                UbicacionId = u.Id,
                SubAlmacenId = sa.Id,
            }).ToListAsync(cancellationToken);
        if (origen.Count == 0) return [];

        var recepcionIds = origen.Select(o => o.RecepcionOrigenId ?? o.MovimientoId).Distinct().ToArray();
        var principales = await db.Movimientos.AsNoTracking().Include(m => m.Lineas)
            .Where(m => recepcionIds.Contains(m.Id)).ToListAsync(cancellationToken);
        var principalPorId = principales.ToDictionary(p => p.Id);

        var lineaIds = origen.Select(o => o.Linea.Id).ToArray();
        var enDevolucion = (await (
            from ld in db.LineasDevolucionProveedor.AsNoTracking()
            join d in db.DevolucionesProveedor.AsNoTracking() on ld.DevolucionId equals d.Id
            where ld.LineaRecepcionOrigenId != null && lineaIds.Contains(ld.LineaRecepcionOrigenId.Value)
                && d.Estado != EstadoDevolucionProveedor.Rechazada
            select new { ld.LineaRecepcionOrigenId, ld.Cantidad }).ToListAsync(cancellationToken))
            .GroupBy(x => x.LineaRecepcionOrigenId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Cantidad));

        var dictamenes = (await db.DictamenesRevision.AsNoTracking()
            .Where(d => lineaIds.Contains(d.LineaRevisionId))
            .OrderBy(d => d.DictaminadoAt).ToListAsync(cancellationToken))
            .GroupBy(d => d.LineaRevisionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var arts = await articulos.ObtenerPorIdsAsync(origen.Select(o => o.Linea.ArticuloId).Distinct().ToArray(), cancellationToken);
        var ocIds = origen.Where(o => o.OcId.HasValue).Select(o => o.OcId!.Value).Distinct().ToArray();
        var folios = ocIds.Length > 0
            ? await ordenesCompra.ObtenerFoliosAsync(ocIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var limites = new Dictionary<DateOnly, DateTimeOffset?>();
        foreach (var fecha in origen.Select(o => principalPorId.GetValueOrDefault(o.RecepcionOrigenId ?? o.MovimientoId)?.FechaMovimiento ?? o.FechaMovimiento).Distinct())
        {
            try { limites[fecha] = await calendario.SumarHorasAsync(fecha, PlazoDictamenHorasHabiles, cancellationToken); }
            catch (BusinessRuleException ex) when (ex.Code == "CALENDARIO_NO_CONFIGURADO") { limites[fecha] = null; }
        }

        var ahora = DateTimeOffset.UtcNow;
        var items = new List<MaterialEnRevisionItem>();
        foreach (var o in origen)
        {
            var recepcionId = o.RecepcionOrigenId ?? o.MovimientoId;
            principalPorId.TryGetValue(recepcionId, out var principal);
            var fechaRecepcion = principal?.FechaMovimiento ?? o.FechaMovimiento;
            var folio = principal?.Folio ?? string.Empty;
            arts.TryGetValue(o.Linea.ArticuloId, out var art);
            string? ocFolio = null;
            if (o.OcId is Guid oc) folios.TryGetValue(oc, out ocFolio);

            var enRevision = o.Linea.Cantidad;
            var historial = dictamenes.GetValueOrDefault(o.Linea.Id) ?? [];
            var dictaminada = historial
                .Where(d => d.Resultado is ResultadoDictamen.Liberar or ResultadoDictamen.Baja)
                .Sum(d => d.Cantidad);
            var devolucion = enDevolucion.GetValueOrDefault(o.Linea.Id);
            var pendiente = enRevision - dictaminada - devolucion;
            if (request.SoloPendientes && pendiente <= 0) continue;

            if (!string.IsNullOrWhiteSpace(request.Q))
            {
                var q = request.Q.Trim();
                var coincide = folio.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || (art?.Clave?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
                if (!coincide) continue;
            }

            var sugerida = o.Linea.LineaOcId is Guid lineaOc
                ? principal?.Lineas.FirstOrDefault(x => x.LineaOcId == lineaOc)?.UbicacionId
                : null;
            var limite = limites.GetValueOrDefault(fechaRecepcion);
            items.Add(new MaterialEnRevisionItem(
                o.Linea.Id, o.MovimientoId, recepcionId, folio, fechaRecepcion, o.OcId, ocFolio,
                o.Linea.ArticuloId, art?.Clave, art?.Descripcion, o.Linea.UnidadMedida,
                enRevision, dictaminada, devolucion, pendiente, o.Linea.ComentarioLinea,
                o.UbicacionId, o.SubAlmacenId, sugerida, limite,
                limite is { } f && f < ahora && pendiente > 0,
                historial.Select(d => new DictamenRevisionItem(
                    d.Id, d.Resultado, d.Cantidad, d.Motivo, d.DictaminadoPor, d.DictaminadoAt,
                    d.DevolucionAProveedorId)).ToList()));
        }
        return items.OrderBy(i => i.FechaRecepcion).ThenBy(i => i.RecepcionFolio).ToList();
    }
}

public sealed class ObtenerLineaRevisionHandler(AlmacenDbContext db)
    : IRequestHandler<ObtenerLineaRevisionQuery, LineaRevisionInfo?>
{
    public async Task<LineaRevisionInfo?> Handle(ObtenerLineaRevisionQuery request, CancellationToken cancellationToken)
    {
        var r = await (
            from l in db.LineasMovimiento.AsNoTracking()
            join m in db.Movimientos.AsNoTracking() on l.MovimientoId equals m.Id
            join u in db.Ubicaciones.AsNoTracking() on l.UbicacionId equals (Guid?)u.Id
            join sa in db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
            where l.Id == request.LineaRevisionId
                && m.Tipo == TipoMovimiento.EntradaCompra
                && m.Estado == EstadoMovimiento.Registrado
                && sa.Tipo == TipoSubAlmacen.MaterialEnRevision
            select new { Linea = l, m.EmpresaId, m.OcId, m.Folio, UbicacionId = u.Id, SubAlmacenId = sa.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (r is null) return null;

        var (_, sucursalId) = await AlmacenSucursalResolver.ResolverAsync(db, r.SubAlmacenId, cancellationToken);
        if (sucursalId is null) return null;
        return new LineaRevisionInfo(
            r.Linea.Id, r.Linea.MovimientoId, r.EmpresaId, sucursalId.Value, r.SubAlmacenId, r.UbicacionId,
            r.Linea.ArticuloId, r.Linea.UnidadMedida, r.Linea.Cantidad, r.Linea.CostoUnitarioMxn,
            r.Linea.LineaOcId, r.OcId, r.Linea.ComentarioLinea, $"{r.Folio} · línea {r.Linea.Posicion}");
    }
}
