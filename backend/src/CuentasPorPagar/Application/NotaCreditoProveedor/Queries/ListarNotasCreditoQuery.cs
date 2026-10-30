using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.NotaCreditoProveedor.Queries;

public sealed record ListarNotasCreditoQuery(
    EstadoNotaCredito? Estado = null,
    Guid? ProveedorId = null,
    Guid? FacturaOrigenId = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null) : IRequest<PagedResponse<NotaCreditoListItemResponse>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "nota_credito_proveedor";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed record NotaCreditoListItemResponse(
    Guid Id,
    string UuidCfdi,
    Guid ProveedorId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaCfdi,
    decimal Total,
    string Moneda,
    int Tipo,
    int TipoRelacionCfdi,
    string UuidRelacionCfdi,
    Guid? FacturaOrigenId,
    decimal SaldoPorAplicar,
    EstadoNotaCredito Estado,
    int Version,
    // Etiqueta resuelta server-side vía read port (ADR-0042).
    string? ProveedorNombre = null, decimal CargoReconocidoPendiente = 0, string? ProveedorRfc = null,
    // La NC 03 formaliza la nota de cargo por subtotal (la nota nace sin IVA).
    decimal Subtotal = 0);

/// <summary>
/// Filtros de la bandeja salvo el estado, compartidos por el listado y por los conteos
/// por estado (UX-F3-05) para que ambos coincidan siempre.
/// </summary>
internal static class NotasCreditoFiltro
{
    public static IQueryable<Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor> Aplicar(CuentasPorPagarDbContext db, ListarNotasCreditoQuery request)
    {
        var q = db.NotasCreditoProveedor.AsNoTracking()
            .Where(x => request.DocumentosPermitidos == null || (request.DocumentosPermitidos ?? Array.Empty<Guid>()).Contains(x.Id));
        if (request.ProveedorId is Guid p) q = q.Where(n => n.ProveedorId == p);
        if (request.FacturaOrigenId is Guid f) q = q.Where(n => n.FacturaOrigenId == f);

        // q se aplica DESPUÉS del filtro de documentos permitidos (alcance por sucursal): solo acota más.
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var patron = LikePatron.Contiene(request.Q);
            q = q.Where(n =>
                EF.Functions.ILike(n.UuidCfdi, patron, LikePatron.Escape)
                || (n.FolioProveedor != null && EF.Functions.ILike(n.FolioProveedor, patron, LikePatron.Escape))
                || (n.SerieProveedor != null && EF.Functions.ILike(n.SerieProveedor, patron, LikePatron.Escape)));
        }

        return q;
    }
}

public sealed record ConteosNotasCreditoPorEstadoQuery(Guid? ProveedorId, Guid? FacturaOrigenId, string? Q = null)
    : IRequest<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>, Millet.SharedKernel.Application.IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.documentos.leer-todas-sucursales";
    public string TipoDocumento => "nota_credito_proveedor";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class ConteosNotasCreditoPorEstadoHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ConteosNotasCreditoPorEstadoQuery, IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>> Handle(
        ConteosNotasCreditoPorEstadoQuery request, CancellationToken cancellationToken)
    {
        var lista = new ListarNotasCreditoQuery(null, request.ProveedorId, request.FacturaOrigenId, 0, 1, request.Q) { DocumentosPermitidos = request.DocumentosPermitidos };
        return await NotasCreditoFiltro.Aplicar(db, lista).GroupBy(x => x.Estado)
            .Select(g => new Millet.SharedKernel.Application.ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ListarNotasCreditoHandler
    : IRequestHandler<ListarNotasCreditoQuery, PagedResponse<NotaCreditoListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarNotasCreditoHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<PagedResponse<NotaCreditoListItemResponse>> Handle(
        ListarNotasCreditoQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = NotasCreditoFiltro.Aplicar(_db, query);
        if (query.Estado is EstadoNotaCredito e) q = q.Where(n => n.Estado == e);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(n => n.FechaCfdi)
            .Skip(offset)
            .Take(limit)
            .Select(n => new NotaCreditoListItemResponse(
                n.Id,
                n.UuidCfdi,
                n.ProveedorId,
                n.FolioProveedor,
                n.SerieProveedor,
                n.FechaCfdi,
                n.Total,
                n.Moneda,
                (int)n.Tipo,
                (int)n.TipoRelacionCfdi,
                n.UuidRelacionCfdi,
                n.FacturaOrigenId,
                n.Total - n.MontoAplicado,
                n.Estado,
                n.Version,
                // Null explícito: expression trees no aceptan args opcionales
                // omitidos (CS0854). Se puebla abajo vía read port.
                null, 0, null, n.Subtotal))
            .ToListAsync(cancellationToken);

        // Etiqueta del proveedor en batch sobre los ids distintos de la
        // página — mismo molde que Compras (ADR-0042).
        var proveedorIds = items.Select(i => i.ProveedorId).Distinct().ToArray();
        if (proveedorIds.Length > 0)
        {
            var nombres = await _proveedores.ObtenerNombresPorIdsAsync(proveedorIds, cancellationToken);
            var rfcs = await _proveedores.ObtenerRfcsPorIdsAsync(proveedorIds, cancellationToken);
            items = items
                .Select(i => i with
                {
                    ProveedorNombre = nombres.GetValueOrDefault(i.ProveedorId),
                    ProveedorRfc = rfcs.GetValueOrDefault(i.ProveedorId),
                })
                .ToList();
        }

        var ncIds = items.Select(i => i.Id).ToArray();
        var cargos = await _db.NotasCargo.AsNoTracking().Where(c => c.NotaCreditoProveedorId != null && ncIds.Contains(c.NotaCreditoProveedorId.Value)).ToListAsync(cancellationToken);
        items = items.Select(i => i with { CargoReconocidoPendiente = Math.Max(0,
            (cargos.FirstOrDefault(c => c.NotaCreditoProveedorId == i.Id)?.Monto ?? 0) - (i.Total - i.SaldoPorAplicar)) }).ToList();
        return new PagedResponse<NotaCreditoListItemResponse>(items, offset, limit, total);
    }
}
