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
    int Limit = 50) : IRequest<PagedResponse<NotaCreditoListItemResponse>>;

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
    string? ProveedorNombre = null);

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

        var q = _db.NotasCreditoProveedor.AsNoTracking();
        if (query.Estado is EstadoNotaCredito e) q = q.Where(n => n.Estado == e);
        if (query.ProveedorId is Guid p) q = q.Where(n => n.ProveedorId == p);
        if (query.FacturaOrigenId is Guid f) q = q.Where(n => n.FacturaOrigenId == f);

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

        return new PagedResponse<NotaCreditoListItemResponse>(items, offset, limit, total);
    }
}
