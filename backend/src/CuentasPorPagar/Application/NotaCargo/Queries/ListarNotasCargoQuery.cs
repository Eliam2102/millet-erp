using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.NotaCargo.Queries;

public sealed record ListarNotasCargoQuery(
    EstadoNotaCargo? Estado = null,
    Guid? ProveedorId = null,
    Guid? FacturaOrigenId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<NotaCargoListItemResponse>>;

public sealed record NotaCargoListItemResponse(
    Guid Id,
    string Folio,
    short FolioAnio,
    Guid ProveedorId,
    Guid? SucursalId,
    string Concepto,
    decimal Monto,
    string Moneda,
    Guid? FacturaOrigenId,
    Guid? DevolucionAProveedorId,
    EstadoNotaCargo Estado,
    DateTimeOffset FechaCreacion,
    int Version,
    // Etiqueta resuelta server-side vía read port (ADR-0042).
    string? ProveedorNombre = null);

public sealed class ListarNotasCargoHandler
    : IRequestHandler<ListarNotasCargoQuery, PagedResponse<NotaCargoListItemResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IProveedorReadPort _proveedores;

    public ListarNotasCargoHandler(CuentasPorPagarDbContext db, IProveedorReadPort proveedores)
    {
        _db = db;
        _proveedores = proveedores;
    }

    public async Task<PagedResponse<NotaCargoListItemResponse>> Handle(
        ListarNotasCargoQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.NotasCargo.AsNoTracking();
        if (query.Estado is EstadoNotaCargo e) q = q.Where(n => n.Estado == e);
        if (query.ProveedorId is Guid p) q = q.Where(n => n.ProveedorId == p);
        if (query.FacturaOrigenId is Guid f) q = q.Where(n => n.FacturaOrigenId == f);

        var total = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(n => n.FechaCreacion)
            .Skip(offset)
            .Take(limit)
            .Select(n => new NotaCargoListItemResponse(
                n.Id,
                n.Folio.Valor,
                n.FolioAnio,
                n.ProveedorId,
                n.SucursalId,
                n.Concepto,
                n.Monto,
                n.Moneda,
                n.FacturaOrigenId,
                n.DevolucionAProveedorId,
                n.Estado,
                n.FechaCreacion,
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

        return new PagedResponse<NotaCargoListItemResponse>(items, offset, limit, total);
    }
}
