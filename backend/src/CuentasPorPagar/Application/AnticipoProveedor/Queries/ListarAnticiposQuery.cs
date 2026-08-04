using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.AnticipoProveedor.Queries;

public sealed record ListarAnticiposQuery(
    EstadoAnticipo? Estado = null,
    Guid? ProveedorId = null,
    Guid? OrdenCompraId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<AnticipoListItemResponse>>;

public sealed record AnticipoListItemResponse(
    Guid Id,
    string UuidCfdi,
    Guid ProveedorId,
    string Serie,
    string? FolioProveedor,
    DateTimeOffset FechaCfdi,
    string Moneda,
    decimal MontoEntregado,
    decimal MontoAmortizado,
    decimal SaldoAmortizable,
    Guid? OrdenCompraId,
    EstadoAnticipo Estado,
    int Version,
    // Etiqueta resuelta server-side vía read port (ADR-0042).
    string? ProveedorNombre = null);

public sealed class ListarAnticiposHandler
    : IRequestHandler<ListarAnticiposQuery, PagedResponse<AnticipoListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarAnticiposHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<PagedResponse<AnticipoListItemResponse>> Handle(
        ListarAnticiposQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.AnticiposProveedor.AsNoTracking();
        if (query.Estado is EstadoAnticipo e) q = q.Where(a => a.Estado == e);
        if (query.ProveedorId is Guid p) q = q.Where(a => a.ProveedorId == p);
        if (query.OrdenCompraId is Guid o) q = q.Where(a => a.OrdenCompraId == o);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(a => a.FechaCaptura)
            .Skip(offset)
            .Take(limit)
            .Select(a => new AnticipoListItemResponse(
                a.Id,
                a.UuidCfdi,
                a.ProveedorId,
                a.Serie,
                a.FolioProveedor,
                a.FechaCfdi,
                a.Moneda,
                a.MontoEntregado,
                a.MontoAmortizado,
                a.MontoEntregado - a.MontoAmortizado,
                a.OrdenCompraId,
                a.Estado,
                a.Version,
                // Null explícito: expression trees no aceptan args opcionales
                // omitidos (CS0854). Se puebla abajo vía read port.
                null))
            .ToListAsync(cancellationToken);

        // Etiqueta del proveedor en batch sobre los ids distintos de la
        // página — mismo molde que Compras (ADR-0042).
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

        return new PagedResponse<AnticipoListItemResponse>(items, offset, limit, total);
    }
}
