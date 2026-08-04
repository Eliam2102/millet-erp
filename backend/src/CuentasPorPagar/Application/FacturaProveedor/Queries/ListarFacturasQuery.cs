using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

public sealed record ListarFacturasQuery(
    EstadoPasivo? Estado = null,
    Guid? ProveedorId = null,
    Guid? SucursalId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<FacturaListItemResponse>>;

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
    // id no resuelve — el FE degrada al GUID abreviado.
    string? ProveedorNombre = null);

public sealed class ListarFacturasHandler : IRequestHandler<ListarFacturasQuery, PagedResponse<FacturaListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarFacturasHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<PagedResponse<FacturaListItemResponse>> Handle(
        ListarFacturasQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.FacturasProveedor.AsNoTracking();
        if (query.Estado is EstadoPasivo e) q = q.Where(f => f.Estado == e);
        if (query.ProveedorId is Guid p) q = q.Where(f => f.ProveedorId == p);
        if (query.SucursalId is Guid s) q = q.Where(f => f.SucursalId == s);

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
                f.Total - f.AnticipoAplicadoTotal - f.NcAplicadasTotal - f.ImportePagado,
                f.Moneda,
                f.Estado,
                f.OrdenCompraId,
                f.Version,
                // Null explícito: expression trees no aceptan args opcionales
                // omitidos (CS0854). Se puebla abajo vía read port.
                null))
            .ToListAsync(cancellationToken);

        // Enriquecer etiqueta del proveedor en batch sobre los ids DISTINTOS
        // de la página — mismo molde que Compras (ADR-0042).
        var proveedorIds = items.Select(i => i.ProveedorId).Distinct().ToArray();
        if (proveedorIds.Length > 0)
        {
            var nombres = await _proveedores.ObtenerNombresPorIdsAsync(proveedorIds, cancellationToken);
            items = items
                .Select(i => nombres.TryGetValue(i.ProveedorId, out var n)
                    ? i with { ProveedorNombre = n }
                    : i)
                .ToList();
        }

        return new PagedResponse<FacturaListItemResponse>(items, offset, limit, total);
    }
}
