using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
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
    decimal MontoTotalMxn);

public sealed record ListarRecepcionesQuery(
    EstadoMovimiento? Estado,
    Guid? SubAlmacenId,
    Guid? OrdenCompraId,
    DateOnly? Desde,
    DateOnly? Hasta,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<RecepcionListItem>>;

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
        IQueryable<MovimientoInventario> query = _db.Movimientos.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimiento.EntradaCompra);
        if (request.Estado is EstadoMovimiento e) query = query.Where(m => m.Estado == e);
        // PR6a: el sub-almacén ya no vive en la cabecera; se filtra vía la vista.
        if (request.SubAlmacenId is Guid sid)
            query = query.Where(m => _db.MovimientosSubAlmacen
                .Any(v => v.MovimientoId == m.Id && v.SubAlmacenId == sid));
        if (request.OrdenCompraId is Guid ocid) query = query.Where(m => m.OcId == ocid);
        if (request.Desde is DateOnly d) query = query.Where(m => m.FechaMovimiento >= d);
        if (request.Hasta is DateOnly h) query = query.Where(m => m.FechaMovimiento <= h);

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

public sealed record ObtenerRecepcionPorIdQuery(Guid Id) : IRequest<RecepcionDetalle?>;

public sealed class ObtenerRecepcionPorIdHandler
    : IRequestHandler<ObtenerRecepcionPorIdQuery, RecepcionDetalle?>
{
    private readonly AlmacenDbContext _db;
    private readonly IArticuloReadPort _articulos;
    private readonly IComprasOcReadPort _ordenesCompra;
    private readonly ICxpDocumentosReadPort _documentosCxp;

    public ObtenerRecepcionPorIdHandler(
        AlmacenDbContext db,
        IArticuloReadPort articulos,
        IComprasOcReadPort ordenesCompra,
        ICxpDocumentosReadPort documentosCxp)
    {
        _db = db;
        _articulos = articulos;
        _ordenesCompra = ordenesCompra;
        _documentosCxp = documentosCxp;
    }

    public async Task<RecepcionDetalle?> Handle(
        ObtenerRecepcionPorIdQuery request, CancellationToken cancellationToken)
    {
        var mov = await _db.Movimientos.AsNoTracking()
            .Include(m => m.Lineas)
            .FirstOrDefaultAsync(m => m.Id == request.Id
                && m.Tipo == TipoMovimiento.EntradaCompra, cancellationToken);
        if (mov is null) return null;

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
        var articuloIds = mov.Lineas.Select(l => l.ArticuloId).Distinct().ToArray();
        var articulos = await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken);

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
            Lineas: mov.Lineas
                .OrderBy(l => l.Posicion)
                .Select(l =>
                {
                    articulos.TryGetValue(l.ArticuloId, out var art);
                    return new RecepcionLineaItem(
                        l.Id, l.Posicion, l.ArticuloId,
                        art?.Clave, art?.Descripcion,
                        l.Cantidad, l.UnidadMedida,
                        l.CostoUnitarioMxn, l.MontoTotalMxn);
                })
                .ToList(),
            CfdiUuidFiscal: cfdiUuidFiscal,
            FacturaFolio: facturaFolio);
    }
}
