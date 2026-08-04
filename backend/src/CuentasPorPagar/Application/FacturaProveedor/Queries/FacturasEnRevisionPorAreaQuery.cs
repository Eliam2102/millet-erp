using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;

/// <summary>
/// Bandeja paginada de facturas en revisión asignadas a una
/// dependencia. Patrón "bandeja por área" (P2 de
/// frontend/patrones-compras.md) — el responsable del área ve solo
/// las facturas que le corresponden, ordenadas por antigüedad para
/// resaltar las que ya cumplieron SLA.
/// </summary>
public sealed record FacturasEnRevisionPorAreaQuery(
    Guid DependenciaRevisoraId,
    Guid? MotivoRevisionId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<FacturaEnRevisionResponse>>;

public sealed record FacturaEnRevisionResponse(
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
    Guid MotivoRevisionId,
    DateTimeOffset FechaEntradaRevision,
    int DiasEnRevision,
    int Version);

public sealed class FacturasEnRevisionPorAreaHandler
    : IRequestHandler<FacturasEnRevisionPorAreaQuery, PagedResponse<FacturaEnRevisionResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public FacturasEnRevisionPorAreaHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<PagedResponse<FacturaEnRevisionResponse>> Handle(
        FacturasEnRevisionPorAreaQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);

        var q = _db.FacturasProveedor.AsNoTracking()
            .Where(f => f.EnRevision && f.DependenciaRevisoraId == query.DependenciaRevisoraId);

        if (query.MotivoRevisionId is Guid m)
            q = q.Where(f => f.MotivoRevisionId == m);

        var total = await q.CountAsync(cancellationToken);

        var ahora = DateTimeOffset.UtcNow;
        var items = await q
            .OrderBy(f => f.FechaEntradaRevision) // más viejas primero
            .Skip(offset)
            .Take(limit)
            .Select(f => new FacturaEnRevisionResponse(
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
                f.MotivoRevisionId!.Value,
                f.FechaEntradaRevision!.Value,
                (int)(ahora - f.FechaEntradaRevision!.Value).TotalDays,
                f.Version))
            .ToListAsync(cancellationToken);

        return new PagedResponse<FacturaEnRevisionResponse>(items, offset, limit, total);
    }
}
