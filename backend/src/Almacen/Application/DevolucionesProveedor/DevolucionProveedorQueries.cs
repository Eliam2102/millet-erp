using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Almacen.Application.DevolucionesProveedor;

// ============================================================================
// Queries de devoluciones a proveedor (F6-PR1). Bandeja con filtros +
// bandeja específica "pendientes de NC fiscal" (cuidado §3 del
// 01-diseno + ciclo bidireccional con CxP).
// ============================================================================

public sealed record DevolucionProveedorListItem(
    Guid Id,
    Guid ProveedorId,
    EstadoDevolucionProveedor Estado,
    decimal MontoTotalMxn,
    DateTimeOffset SolicitadaAt,
    DateTimeOffset? AutorizadaAt,
    DateTimeOffset? RegistradaAt,
    DateTimeOffset? ConciliadaConNcFiscalAt,
    string? FolioMovimientoSalida,
    // Enriquecimiento ADR-0042: razón social resuelta vía IProveedorReadPort.
    // Opcional (default null) → construcciones previas siguen compilando;
    // null si el puerto no resuelve (FE cae al id truncado).
    string? ProveedorNombre = null);

public sealed record DevolucionProveedorDetalle(
    Guid Id,
    Guid ProveedorId,
    Guid? RecepcionOrigenId,
    Guid? FacturaProveedorOrigenId,
    Guid? OrdenCompraOrigenId,
    Guid? SubAlmacenOrigenId,
    EstadoDevolucionProveedor Estado,
    string Motivo,
    Guid SolicitadaPor,
    DateTimeOffset SolicitadaAt,
    Guid? AutorizadaPor,
    DateTimeOffset? AutorizadaAt,
    string? MotivoRechazo,
    DateTimeOffset? RechazadaAt,
    Guid? MovimientoSalidaId,
    string? FolioMovimientoSalida,
    DateTimeOffset? RegistradaAt,
    Guid? NotaCreditoFiscalId,
    DateTimeOffset? ConciliadaConNcFiscalAt,
    int Version,
    IReadOnlyList<DevolucionProveedorLineaItem> Lineas,
    IReadOnlyList<DevolucionProveedorEvidenciaItem> Evidencias,
    // Enriquecimiento ADR-0042 (opcional, default null — ver ListItem).
    string? ProveedorNombre = null,
    // Folio de la NC fiscal que concilió la devolución, resuelto vía
    // ICxpDocumentosReadPort; null si el puerto no resuelve o la NC no
    // tiene folio del proveedor (FE cae al id truncado).
    string? NotaCreditoFolio = null);

public sealed record DevolucionProveedorLineaItem(
    Guid Id,
    int Posicion,
    Guid ArticuloId,
    decimal Cantidad,
    string UnidadMedida,
    decimal CostoUnitarioMxn,
    decimal MontoTotalMxn,
    Guid? LineaRecepcionOrigenId,
    // Enriquecimiento ADR-0042: clave + descripción del artículo vía
    // IArticuloReadPort en batch (mismo patrón que RecepcionLineaItem).
    string? ArticuloClave = null,
    string? ArticuloDescripcion = null);

public sealed record DevolucionProveedorEvidenciaItem(
    Guid Id,
    string TipoEvidencia,
    string NombreArchivo,
    string BlobRef,
    string? Comentario);

public sealed record ListarDevolucionesProveedorQuery(
    EstadoDevolucionProveedor? Estado,
    Guid? ProveedorId,
    bool? SoloPendientesNcFiscal,
    int Offset,
    int Limit) : IRequest<AlmacenPagedResponse<DevolucionProveedorListItem>>;

public sealed class ListarDevolucionesProveedorHandler
    : IRequestHandler<ListarDevolucionesProveedorQuery, AlmacenPagedResponse<DevolucionProveedorListItem>>
{
    private readonly AlmacenDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarDevolucionesProveedorHandler(AlmacenDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<AlmacenPagedResponse<DevolucionProveedorListItem>> Handle(
        ListarDevolucionesProveedorQuery request, CancellationToken cancellationToken)
    {
        IQueryable<DevolucionAProveedor> query = _db.Set<DevolucionAProveedor>().AsNoTracking();
        if (request.Estado is EstadoDevolucionProveedor e) query = query.Where(d => d.Estado == e);
        if (request.ProveedorId is Guid pid) query = query.Where(d => d.ProveedorId == pid);
        if (request.SoloPendientesNcFiscal is true)
            query = query.Where(d => d.Estado == EstadoDevolucionProveedor.Registrada);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(d => d.SolicitadaAt)
            .Skip(request.Offset).Take(request.Limit)
            // Los expression trees no admiten argumentos opcionales (CS0854):
            // ProveedorNombre va explícito en null y se enriquece abajo.
            .Select(d => new DevolucionProveedorListItem(
                d.Id,
                d.ProveedorId,
                d.Estado,
                d.Lineas.Sum(l => l.MontoTotalMxn),
                d.SolicitadaAt,
                d.AutorizadaAt,
                d.RegistradaAt,
                d.ConciliadaConNcFiscalAt,
                d.FolioMovimientoSalida,
                null))
            .ToListAsync(cancellationToken);

        // Razón social del proveedor: batch sobre los ids distintos de la
        // página (anti-N+1, ADR-0042). Si el puerto no resuelve → null y el
        // FE cae al id truncado.
        var proveedorIds = items.Select(i => i.ProveedorId).Distinct().ToArray();
        var proveedores = await _proveedores.ObtenerPorIdsAsync(proveedorIds, cancellationToken);
        var enriquecidos = items
            .Select(i => proveedores.TryGetValue(i.ProveedorId, out var p)
                ? i with { ProveedorNombre = p.RazonSocial }
                : i)
            .ToList();

        return new AlmacenPagedResponse<DevolucionProveedorListItem>(
            enriquecidos, request.Offset, request.Limit, total);
    }
}

public sealed record ObtenerDevolucionProveedorPorIdQuery(Guid Id) : IRequest<DevolucionProveedorDetalle?>;

public sealed class ObtenerDevolucionProveedorPorIdHandler
    : IRequestHandler<ObtenerDevolucionProveedorPorIdQuery, DevolucionProveedorDetalle?>
{
    private readonly AlmacenDbContext _db;
    private readonly IProveedorReadPort _proveedores;
    private readonly IArticuloReadPort _articulos;
    private readonly ICxpDocumentosReadPort _documentosCxp;

    public ObtenerDevolucionProveedorPorIdHandler(
        AlmacenDbContext db,
        IProveedorReadPort proveedores,
        IArticuloReadPort articulos,
        ICxpDocumentosReadPort documentosCxp)
    {
        _db = db;
        _proveedores = proveedores;
        _articulos = articulos;
        _documentosCxp = documentosCxp;
    }

    public async Task<DevolucionProveedorDetalle?> Handle(
        ObtenerDevolucionProveedorPorIdQuery request, CancellationToken cancellationToken)
    {
        var d = await _db.Set<DevolucionAProveedor>().AsNoTracking()
            .Include(x => x.Lineas)
            .Include(x => x.Evidencias)
            .FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken);
        if (d is null) return null;

        // Enriquecimiento de nombres (ADR-0042). Cada resolución cae al id
        // crudo si el read-port no la encuentra (campo null en el DTO).
        var proveedor = await _proveedores.ObtenerAsync(d.ProveedorId, cancellationToken);
        var articuloIds = d.Lineas.Select(l => l.ArticuloId).Distinct().ToArray();
        var articulos = await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken);

        // Folio de la NC fiscal que concilió (batch-de-1, mismo patrón que
        // el folio de OC en recepciones).
        string? notaCreditoFolio = null;
        if (d.NotaCreditoFiscalId is Guid ncId)
        {
            var foliosNc = await _documentosCxp.ObtenerFoliosNotaCreditoAsync(
                new[] { ncId }, cancellationToken);
            foliosNc.TryGetValue(ncId, out notaCreditoFolio);
        }

        return new DevolucionProveedorDetalle(
            Id: d.Id,
            ProveedorId: d.ProveedorId,
            RecepcionOrigenId: d.RecepcionOrigenId,
            FacturaProveedorOrigenId: d.FacturaProveedorOrigenId,
            OrdenCompraOrigenId: d.OrdenCompraOrigenId,
            SubAlmacenOrigenId: d.SubAlmacenOrigenId,
            Estado: d.Estado,
            Motivo: d.Motivo,
            SolicitadaPor: d.SolicitadaPor,
            SolicitadaAt: d.SolicitadaAt,
            AutorizadaPor: d.AutorizadaPor,
            AutorizadaAt: d.AutorizadaAt,
            MotivoRechazo: d.MotivoRechazo,
            RechazadaAt: d.RechazadaAt,
            MovimientoSalidaId: d.MovimientoSalidaId,
            FolioMovimientoSalida: d.FolioMovimientoSalida,
            RegistradaAt: d.RegistradaAt,
            NotaCreditoFiscalId: d.NotaCreditoFiscalId,
            ConciliadaConNcFiscalAt: d.ConciliadaConNcFiscalAt,
            Version: d.Version,
            Lineas: d.Lineas.OrderBy(l => l.Posicion)
                .Select(l =>
                {
                    articulos.TryGetValue(l.ArticuloId, out var art);
                    return new DevolucionProveedorLineaItem(
                        l.Id, l.Posicion, l.ArticuloId, l.Cantidad, l.UnidadMedida,
                        l.CostoUnitarioMxn, l.MontoTotalMxn, l.LineaRecepcionOrigenId,
                        art?.Clave, art?.Descripcion);
                })
                .ToList(),
            Evidencias: d.Evidencias
                .Select(e => new DevolucionProveedorEvidenciaItem(
                    e.Id, e.TipoEvidencia, e.NombreArchivo, e.BlobRef, e.Comentario))
                .ToList(),
            ProveedorNombre: proveedor?.RazonSocial,
            NotaCreditoFolio: notaCreditoFolio);
    }
}
