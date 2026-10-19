using MediatR;
using Millet.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.Recepciones;

// ============================================================================
// Queries de Recepciones (F2-PR2). Bandeja paginada + detalle.
// Filtra `movimientos_inventario` por tipo = EntradaCompra.
// ============================================================================

public sealed record RecepcionListItem(
    Guid Id,
    string Folio,
    DateOnly FechaMovimiento,
    Guid SubAlmacenId,
    Guid? OrdenCompraId,
    // Folio de la OC resuelto en backend (ADR-0042). Null = el read-port no
    // resolvió; el frontend cae al id (truncado).
    string? OrdenCompraFolio,
    Guid? CfdiRecibidoId,
    decimal MontoTotalMxn,
    EstadoMovimiento Estado);

public sealed record RecepcionDetalle(
    Guid Id,
    string Folio,
    DateOnly FechaMovimiento,
    Guid SubAlmacenId,
    // Nombres resueltos en backend (ADR-0042). Null = el read-port/lookup no
    // resolvió; el frontend cae al id/clave cruda.
    string? SubAlmacenClave,
    string? SubAlmacenNombre,
    Guid? OrdenCompraId,
    string? OrdenCompraFolio,
    Guid? CfdiRecibidoId,
    Guid? FacturaId,
    EstadoMovimiento Estado,
    int Version,
    string? Observaciones,
    DateTimeOffset? RegistradoAt,
    Guid? RegistradoPor,
    IReadOnlyList<RecepcionLineaItem> Lineas,
    // Referencias CxP resueltas vía ICxpDocumentosReadPort (ADR-0042):
    // UUID fiscal del CFDI (variante A) y folio de la factura del proveedor
    // (variante B). Opcionales (default null) → construcciones previas
    // siguen compilando; null si el puerto no resuelve (FE cae al id
    // truncado).
    string? CfdiUuidFiscal = null,
    string? FacturaFolio = null);

public sealed record RecepcionLineaItem(
    Guid Id,
    int Posicion,
    Guid ArticuloId,
    // Resueltos en backend (ADR-0042). Null = el read-port no resolvió.
    string? ArticuloClave,
    string? ArticuloDescripcion,
    decimal Cantidad,
    string UnidadMedida,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn, decimal? CantidadCapturada = null, string? UnidadCapturada = null,
    Guid? CentroCostoId = null, string? CentroCostoClave = null, string? CentroCostoNombre = null,
    // Rack destino de la línea (bin N4) y su ruta "ALM › SUB · BIN" (mismo formato que
    // los selectores). Null = línea histórica sin bin o bin no resuelto.
    Guid? UbicacionId = null, string? UbicacionClave = null,
    // ALM-04: línea que entró al sub-almacén de material en revisión (no disponible hasta el dictamen) y su motivo de daño.
    bool EnRevision = false, string? MotivoDano = null);

public sealed record ListarRecepcionesQuery(
    EstadoMovimiento? Estado,
    Guid? SubAlmacenId,
    Guid? OrdenCompraId,
    DateOnly? Desde,
    DateOnly? Hasta,
    int Offset,
    int Limit,
    Guid? ProveedorId = null,
    string? Q = null) : IRequest<AlmacenPagedResponse<RecepcionListItem>>, IDocumentoScopedQuery
{
    public string TipoDocumento => "recepcion";
    public string PermisoTodasSucursales => "almacen.entradas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ListarRecepcionesHandler
    : IRequestHandler<ListarRecepcionesQuery, AlmacenPagedResponse<RecepcionListItem>>
{
    private readonly AlmacenDbContext _db;
    private readonly IComprasOcReadPort _ordenesCompra;

    public ListarRecepcionesHandler(AlmacenDbContext db, IComprasOcReadPort ordenesCompra)
    {
        _db = db;
        _ordenesCompra = ordenesCompra;
    }

    public async Task<AlmacenPagedResponse<RecepcionListItem>> Handle(
        ListarRecepcionesQuery request, CancellationToken cancellationToken)
    {
        var query = await RecepcionesFiltro.AplicarAsync(_db, _ordenesCompra, request, cancellationToken);
        if (request.Estado is EstadoMovimiento e) query = query.Where(m => m.Estado == e);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.FechaMovimiento).ThenByDescending(m => m.FechaRegistro)
            .Skip(request.Offset).Take(request.Limit)
            .Select(m => new RecepcionListItem(
                m.Id,
                m.Folio ?? "(borrador)",
                m.FechaMovimiento,
                // PR6a: sub-almacén derivado de la línea vía la vista.
                _db.MovimientosSubAlmacen
                    .Where(v => v.MovimientoId == m.Id)
                    .Select(v => v.SubAlmacenId)
                    .FirstOrDefault(),
                m.OcId,
                null, // OrdenCompraFolio: se enriquece en batch abajo (ADR-0042).
                m.CfdiRecibidoId,
                m.Lineas.Sum(l => l.MontoTotalMxn),
                m.Estado))
            .ToListAsync(cancellationToken);

        // Folio de OC: batch sobre los OcId distintos de la página (anti-N+1,
        // state-agnostic: resuelve aunque la OC ya esté Cerrada/Cancelada).
        var ocIds = items
            .Where(i => i.OrdenCompraId is not null)
            .Select(i => i.OrdenCompraId!.Value)
            .Distinct()
            .ToArray();
        var folios = ocIds.Length > 0
            ? await _ordenesCompra.ObtenerFoliosAsync(ocIds, cancellationToken)
            : new Dictionary<Guid, string>();

        var enriquecidos = items
            .Select(i => i.OrdenCompraId is Guid oid && folios.TryGetValue(oid, out var folio)
                ? i with { OrdenCompraFolio = folio }
                : i)
            .ToList();

        return new AlmacenPagedResponse<RecepcionListItem>(
            enriquecidos, request.Offset, request.Limit, total);
    }
}

/// <summary>
/// Filtros de la bandeja de recepciones salvo el estado, compartidos por el listado
/// y por los conteos por estado (UX-F3-05) para que ambos coincidan siempre.
/// </summary>
internal static class RecepcionesFiltro
{
    public static async Task<IQueryable<MovimientoInventario>> AplicarAsync(
        AlmacenDbContext db, IComprasOcReadPort ordenesCompra, ListarRecepcionesQuery request, CancellationToken cancellationToken)
    {
        var query = db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra)
            // ALM-04: el movimiento de material en revisión es parte de la recepción principal; no se lista aparte.
            .Where(m => m.RecepcionOrigenId == null);
        if (request.DocumentosPermitidos is { } permitidos) query = query.Where(x => permitidos.Contains(x.Id));
        // PR6a: el sub-almacén ya no vive en la cabecera; se filtra vía la vista.
        if (request.SubAlmacenId is Guid sid)
            query = query.Where(m => db.MovimientosSubAlmacen
                .Any(v => v.MovimientoId == m.Id && v.SubAlmacenId == sid));
        if (request.OrdenCompraId is Guid ocid) query = query.Where(m => m.OcId == ocid);
        if (request.Desde is DateOnly d) query = query.Where(m => m.FechaMovimiento >= d);
        if (request.Hasta is DateOnly h) query = query.Where(m => m.FechaMovimiento <= h);
        // Proveedor: la recepción no lo guarda; se resuelve vía las OC del proveedor (puerto de Compras).
        if (request.ProveedorId is Guid pid)
        {
            var ocsProveedor = (await ordenesCompra.ObtenerIdsPorProveedorAsync(pid, cancellationToken)).ToArray();
            query = query.Where(m => m.OcId != null && ocsProveedor.Contains(m.OcId.Value));
        }
        // Búsqueda por folio de la recepción (server-side); los folios se generan en mayúsculas.
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var q = request.Q.Trim().ToUpperInvariant();
            query = query.Where(m => m.Folio != null && m.Folio.Contains(q));
        }

        return query;
    }
}

public sealed record ConteosRecepcionesPorEstadoQuery(
    Guid? SubAlmacenId, Guid? OrdenCompraId, DateOnly? Desde, DateOnly? Hasta, Guid? ProveedorId = null, string? Q = null)
    : IRequest<IReadOnlyList<ConteoPorEstadoItem>>, IDocumentoScopedQuery
{
    public string TipoDocumento => "recepcion";
    public string PermisoTodasSucursales => "almacen.entradas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ConteosRecepcionesPorEstadoHandler(AlmacenDbContext db, IComprasOcReadPort ordenesCompra)
    : IRequestHandler<ConteosRecepcionesPorEstadoQuery, IReadOnlyList<ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<ConteoPorEstadoItem>> Handle(
        ConteosRecepcionesPorEstadoQuery request, CancellationToken cancellationToken)
    {
        var lista = new ListarRecepcionesQuery(null, request.SubAlmacenId, request.OrdenCompraId, request.Desde, request.Hasta,
            0, 0, request.ProveedorId, request.Q) { DocumentosPermitidos = request.DocumentosPermitidos };
        var query = await RecepcionesFiltro.AplicarAsync(db, ordenesCompra, lista, cancellationToken);
        return await query.GroupBy(m => m.Estado)
            .Select(g => new ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}

public sealed record ObtenerRecepcionPorIdQuery(Guid Id) : IRequest<RecepcionDetalle?>;

public sealed class ObtenerRecepcionPorIdHandler
    : IRequestHandler<ObtenerRecepcionPorIdQuery, RecepcionDetalle?>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;
    private readonly IComprasOcReadPort _ordenesCompra;
    private readonly ICxpDocumentosReadPort _documentosCxp;
    private readonly ICentroCostoReadPort _centros;

    public ObtenerRecepcionPorIdHandler(
        AlmacenDbContext db,
        IArticuloReadPort articulos,
        IComprasOcReadPort ordenesCompra,
        ICxpDocumentosReadPort documentosCxp, ICentroCostoReadPort centros)
    {
        _db = db;
        _articulos = articulos;
        _ordenesCompra = ordenesCompra;
        _documentosCxp = documentosCxp;
        _centros = centros;
    }

    public async Task<RecepcionDetalle?> Handle(
        ObtenerRecepcionPorIdQuery request, CancellationToken cancellationToken)
    {
        var mov = await _db.Movimientos.AsNoTracking()
            .Include(m => m.Lineas)
            .FirstOrDefaultAsync(m => m.Id == request.Id
                && m.Tipo == TipoMovimiento.EntradaCompra, cancellationToken);
        if (mov is null) return null;

        // ALM-04: las líneas del movimiento de revisión (hermano) se muestran dentro de la recepción principal.
        var hermanos = await _db.Movimientos.AsNoTracking()
            .Include(m => m.Lineas)
            .Where(m => m.RecepcionOrigenId == mov.Id && m.Tipo == TipoMovimiento.EntradaCompra && m.Estado == EstadoMovimiento.Registrado)
            .ToListAsync(cancellationToken);
        var lineas = mov.Lineas.OrderBy(l => l.Posicion)
            .Concat(hermanos.SelectMany(h => h.Lineas.OrderBy(l => l.Posicion)))
            .ToList();

        // Enriquecimiento de nombres (ADR-0042). Cada resolución cae al id/clave
        // cruda si el read-port no la encuentra (campo null en el DTO).

        // PR6a: sub-almacén derivado de la ubicación de la línea vía la vista.
        var subAlmacenId = await _db.MovimientosSubAlmacen.AsNoTracking()
            .Where(v => v.MovimientoId == mov.Id)
            .Select(v => v.SubAlmacenId)
            .FirstOrDefaultAsync(cancellationToken);

        // Sub-almacén: catálogo intra-módulo, lookup local (1 query).
        var subAlmacen = await _db.SubAlmacenes.AsNoTracking()
            .Where(s => s.Id == subAlmacenId)
            .Select(s => new { s.Clave, s.Nombre })
            .FirstOrDefaultAsync(cancellationToken);

        // Artículo: batch sobre los ids distintos de las líneas (anti-N+1).
        var articuloIds = lineas.Select(l => l.ArticuloId).Distinct().ToArray();
        var articulos = await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken);

        var centroIds = lineas.Where(x => x.CentroCostoId.HasValue).Select(x => x.CentroCostoId!.Value).Distinct().ToArray();
        var centros = await _centros.ObtenerAsync(centroIds, cancellationToken);

        // Ruta del rack de cada línea: una consulta con los bins distintos (anti-N+1).
        var ubicacionIds = lineas
            .Where(l => l.UbicacionId is not null)
            .Select(l => l.UbicacionId!.Value)
            .Distinct()
            .ToArray();
        Dictionary<Guid, string> rutasUbicacion = ubicacionIds.Length > 0
            ? (await (
                from u in _db.Ubicaciones.AsNoTracking()
                join sa in _db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
                join al in _db.Almacenes.AsNoTracking() on sa.AlmacenId equals al.Id
                where ubicacionIds.Contains(u.Id)
                select new { u.Id, u.Clave, SubClave = sa.Clave, AlmacenClave = al.Clave })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.Id, x => $"{x.AlmacenClave} › {x.SubClave} · {x.Clave}")
            : new Dictionary<Guid, string>();

        var binsRevision = ubicacionIds.Length > 0
            ? await (from u in _db.Ubicaciones.AsNoTracking()
                     join sa in _db.SubAlmacenes.AsNoTracking() on u.SubAlmacenId equals sa.Id
                     where ubicacionIds.Contains(u.Id) && sa.Tipo == TipoSubAlmacen.MaterialEnRevision
                     select u.Id).ToListAsync(cancellationToken)
            : [];

        // Folio de OC: lectura de presentación state-agnostic (resuelve aunque
        // la OC ya esté Cerrada/Cancelada). Batch aunque la recepción tenga 1 OC.
        string? ordenCompraFolio = null;
        if (mov.OcId is Guid ocId)
        {
            var folios = await _ordenesCompra.ObtenerFoliosAsync(new[] { ocId }, cancellationToken);
            folios.TryGetValue(ocId, out ordenCompraFolio);
        }

        // Referencias CxP (mismo patrón batch-de-1 que el folio de OC):
        // UUID fiscal del CFDI referenciado (variante A) y folio de la
        // factura del proveedor (variante B). Si la recepción se registró
        // con folio fiscal capturado a mano (CFDI aún no en CxP), ese UUID
        // persistido es el fallback.
        string? cfdiUuidFiscal = mov.CfdiUuidFiscal;
        if (mov.CfdiRecibidoId is Guid cfdiId)
        {
            var uuids = await _documentosCxp.ObtenerUuidsFiscalesCfdiAsync(
                new[] { cfdiId }, cancellationToken);
            if (uuids.TryGetValue(cfdiId, out var uuidResuelto))
            {
                cfdiUuidFiscal = uuidResuelto;
            }
        }

        string? facturaFolio = null;
        if (mov.FacturaId is Guid facturaId)
        {
            var foliosFactura = await _documentosCxp.ObtenerFoliosFacturaAsync(
                new[] { facturaId }, cancellationToken);
            foliosFactura.TryGetValue(facturaId, out facturaFolio);
        }

        return new RecepcionDetalle(
            Id: mov.Id,
            Folio: mov.Folio ?? "(borrador)",
            FechaMovimiento: mov.FechaMovimiento,
            SubAlmacenId: subAlmacenId,
            SubAlmacenClave: subAlmacen?.Clave,
            SubAlmacenNombre: subAlmacen?.Nombre,
            OrdenCompraId: mov.OcId,
            OrdenCompraFolio: ordenCompraFolio,
            CfdiRecibidoId: mov.CfdiRecibidoId,
            FacturaId: mov.FacturaId,
            Estado: mov.Estado,
            Version: mov.Version,
            Observaciones: mov.ComentarioLibre,
            RegistradoAt: mov.RegistradoAt,
            RegistradoPor: mov.RegistradoPor,
            Lineas: lineas
                .Select((l, i) =>
                {
                    var enRevision = l.UbicacionId is Guid bin && binsRevision.Contains(bin);
                    articulos.TryGetValue(l.ArticuloId, out var art);
                    var centro = l.CentroCostoId is Guid cc ? centros.GetValueOrDefault(cc) : null;
                    return new RecepcionLineaItem(
                        l.Id, i + 1, l.ArticuloId,
                        art?.Clave, art?.Descripcion,
                        l.Cantidad, l.UnidadMedida,
                        l.CostoUnitarioMxn, l.MontoTotalMxn, l.CantidadCapturada, l.UnidadCapturada,
                        l.CentroCostoId, centro?.Clave, centro?.Nombre,
                        l.UbicacionId,
                        l.UbicacionId is Guid ub && rutasUbicacion.TryGetValue(ub, out var ruta) ? ruta : null,
                        enRevision, enRevision ? l.ComentarioLinea : null);
                })
                .ToList(),
            CfdiUuidFiscal: cfdiUuidFiscal,
            FacturaFolio: facturaFolio);
    }
}
