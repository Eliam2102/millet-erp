using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

public sealed record ListarFacturasQuery(
    EstadoPasivo? Estado = null,
    Guid? ProveedorId = null,
    Guid? SucursalId = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null) : IRequest<PagedResponse<FacturaListItemResponse>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.facturas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed record FacturaListItemResponse(
    Guid Id,
    Guid ProveedorId,
    Guid SucursalId,
    string? FolioProveedor,
    string? SerieProveedor,
    DateTimeOffset FechaDocumento,
    DateOnly FechaVencimiento,
    decimal Total,
    decimal SaldoPendiente,
    string Moneda,
    EstadoPasivo Estado,
    Guid? OrdenCompraId,
    int Version,
    // Etiqueta resuelta server-side vía read port (ADR-0042); null si el
    // id no resuelve — el FE indica Por confirmar.
    string? ProveedorNombre = null, string? UuidCfdi = null, string? OrdenCompraFolio = null, string? ProveedorRfc = null);

/// <summary>
/// Filtros de la bandeja salvo el estado, compartidos por el listado y por los conteos
/// por estado (UX-F3-05) para que ambos coincidan siempre.
/// </summary>
internal static class FacturasFiltro
{
    public static IQueryable<Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor> Aplicar(CuentasPorPagarDbContext db, ListarFacturasQuery request)
    {
        var q = db.FacturasProveedor
            .Where(x => request.SucursalesPermitidas == null || request.SucursalesPermitidas.Contains(x.SucursalId)).AsNoTracking();
        if (request.ProveedorId is Guid p) q = q.Where(f => f.ProveedorId == p);
        if (request.SucursalId is Guid s) q = q.Where(f => f.SucursalId == s);

        // q se aplica DESPUÉS del filtro de sucursal (SucursalesPermitidas): solo acota más, nunca amplía el alcance.
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var patron = LikePatron.Contiene(request.Q);
            q = q.Where(f =>
                (f.FolioProveedor != null && EF.Functions.ILike(f.FolioProveedor, patron, LikePatron.Escape))
                || (f.SerieProveedor != null && EF.Functions.ILike(f.SerieProveedor, patron, LikePatron.Escape))
                || (f.UuidCfdi != null && EF.Functions.ILike(f.UuidCfdi, patron, LikePatron.Escape)));
        }

        return q;
    }
}

public sealed record ConteosFacturasPorEstadoQuery(Guid? ProveedorId, Guid? SucursalId, string? Q = null)
    : IRequest<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_pagar.facturas.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
}

public sealed class ConteosFacturasPorEstadoHandler(CuentasPorPagarDbContext db)
    : IRequestHandler<ConteosFacturasPorEstadoQuery, IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>
{
    public async Task<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>> Handle(
        ConteosFacturasPorEstadoQuery request, CancellationToken cancellationToken)
    {
        var lista = new ListarFacturasQuery(null, request.ProveedorId, request.SucursalId, 0, 1, request.Q) { SucursalesPermitidas = request.SucursalesPermitidas };
        return await FacturasFiltro.Aplicar(db, lista).GroupBy(x => x.Estado)
            .Select(g => new Millet.SharedKernel.Application.ConteoPorEstadoItem((int)g.Key, g.Count()))
            .ToListAsync(cancellationToken);
    }
}

public sealed class ListarFacturasHandler : IRequestHandler<ListarFacturasQuery, PagedResponse<FacturaListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;
    private readonly IComprasOcReadPort _ordenes;

    public ListarFacturasHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores, IComprasOcReadPort ordenes)
    {
        _db = db;
        _proveedores = proveedores;
        _ordenes = ordenes;
    }

    public async Task<PagedResponse<FacturaListItemResponse>> Handle(
        ListarFacturasQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = FacturasFiltro.Aplicar(_db, query);
        if (query.Estado is EstadoPasivo e) q = q.Where(f => f.Estado == e);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(f => f.FechaDocumento)
            .Skip(offset)
            .Take(limit)
            .Select(f => new FacturaListItemResponse(
                f.Id,
                f.ProveedorId,
                f.SucursalId,
                f.FolioProveedor,
                f.SerieProveedor,
                f.FechaDocumento,
                f.FechaVencimiento,
                f.Total,
                f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.CargosAplicadosTotal - f.ImportePagado,
                f.Moneda,
                f.Estado,
                f.OrdenCompraId,
                f.Version,
                // Null explícito: expression trees no aceptan args opcionales
                // omitidos (CS0854). Se puebla abajo vía read port.
                null, f.UuidCfdi, null, null))
            .ToListAsync(cancellationToken);

        // Enriquecer etiqueta del proveedor en batch sobre los ids DISTINTOS
        // de la página — mismo molde que Compras (ADR-0042).
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

        var ocIds = items.Where(i => i.OrdenCompraId != null).Select(i => i.OrdenCompraId!.Value).Distinct().ToArray();
        if (ocIds.Length > 0)
        {
            var folios = await _ordenes.ObtenerFoliosAsync(ocIds, cancellationToken);
            items = items.Select(i => i with
            {
                OrdenCompraFolio = i.OrdenCompraId is Guid id ? folios.GetValueOrDefault(id) : null,
            }).ToList();
        }
        return new PagedResponse<FacturaListItemResponse>(items, offset, limit, total);
    }
}
