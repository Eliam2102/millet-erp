using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;

namespace Millet.Facturacion.Application.Facturas.Queries;

/// <summary>
/// Bandeja de facturas de venta (§7.2 diseño), con filtro por estado y
/// paginación. CAJAS-PR2: filtrada por la Capa A de Cajas;
/// <paramref name="SoloSinAsignar"/> restringe al bucket "Sin asignar"
/// (requiere <c>facturacion.caja.leer-todas</c>).
/// </summary>
public sealed record BandejaFacturasQuery(
    EstadoTimbrado? Estado,
    int Offset,
    int Limit,
    bool SoloSinAsignar = false) : IRequest<BandejaFacturasResponse>;

public sealed record BandejaFacturasResponse(
    IReadOnlyList<FacturaBandejaItem> Items,
    int? SinAsignarCount);

public sealed record FacturaBandejaItem(
    Guid Id,
    string Folio,
    string Estado,
    string? Uuid,
    string ReceptorNombre,
    string ReceptorRfc,
    decimal Total,
    string Moneda,
    DateTimeOffset? FechaTimbrado);

public sealed class BandejaFacturasHandler
    : IRequestHandler<BandejaFacturasQuery, BandejaFacturasResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public BandejaFacturasHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<BandejaFacturasResponse> Handle(
        BandejaFacturasQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var offset = Math.Max(0, query.Offset);

        var q = _db.FacturasVenta.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(f => f.Estado == estado);

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var rows = await q
            .OrderByDescending(f => f.FolioNumero)
            .Skip(offset)
            .Take(limit)
            .Select(f => new
            {
                f.Id, f.Folio, f.Estado, f.Uuid, f.ReceptorNombre,
                f.ReceptorRfc, f.Total, f.Moneda, f.FechaTimbrado
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new FacturaBandejaItem(
                r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.ReceptorNombre,
                r.ReceptorRfc, r.Total, r.Moneda, r.FechaTimbrado))
            .ToList();

        return new BandejaFacturasResponse(items, sinAsignarCount);
    }
}
