using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.NotasCredito.Queries;

// ---- Bandeja (CAJAS-PR2) ----

/// <summary>
/// Bandeja de notas de crédito (CAJAS-PR2 — no existía; entra con la Capa A
/// para que la familia quede filtrada desde su primera lectura).
/// <paramref name="SoloSinAsignar"/> restringe al bucket "Sin asignar"
/// (requiere <c>facturacion.caja.leer-todas</c>).
/// </summary>
public sealed record BandejaNotasCreditoQuery(
    EstadoTimbrado? Estado, int Offset, int Limit, bool SoloSinAsignar = false)
    : IRequest<BandejaNotasCreditoResponse>;

public sealed record BandejaNotasCreditoResponse(
    IReadOnlyList<NotaCreditoBandejaItem> Items,
    int? SinAsignarCount);

public sealed record NotaCreditoBandejaItem(
    Guid Id, string Folio, string Estado, string? Uuid, string ReceptorNombre, string ReceptorRfc,
    string Motivo, decimal Total, string Moneda, DateTimeOffset? FechaTimbrado);

public sealed class BandejaNotasCreditoHandler
    : IRequestHandler<BandejaNotasCreditoQuery, BandejaNotasCreditoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public BandejaNotasCreditoHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<BandejaNotasCreditoResponse> Handle(
        BandejaNotasCreditoQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var q = _db.NotasCredito.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(n => n.Estado == estado);

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var rows = await q
            .OrderByDescending(n => n.FolioNumero)
            .Skip(Math.Max(0, query.Offset)).Take(limit)
            .Select(n => new
            {
                n.Id, n.Folio, n.Estado, n.Uuid, n.ReceptorNombre, n.ReceptorRfc,
                n.Motivo, n.Total, n.Moneda, n.FechaTimbrado
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new NotaCreditoBandejaItem(
            r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.ReceptorNombre, r.ReceptorRfc,
            r.Motivo.ToString(), r.Total, r.Moneda, r.FechaTimbrado)).ToList();

        return new BandejaNotasCreditoResponse(items, sinAsignarCount);
    }
}

// ---- Detalle (CAJAS-PR2) ----

/// <summary>Detalle de una nota de crédito. Fuera de alcance → 404.</summary>
public sealed record NotaCreditoDetalleQuery(Guid Id) : IRequest<NotaCreditoDetalleResponse>;

public sealed record NotaCreditoDetalleResponse(
    Guid Id, string Folio, string Estado, string? Uuid, string ReceptorNombre, string ReceptorRfc,
    string Motivo, Guid? FacturaRelacionadaId, Guid? AnticipoOrigenId, string Descripcion,
    decimal Subtotal, decimal ImpuestosTrasladados, decimal Total, string Moneda,
    DateTimeOffset? FechaTimbrado, int Version,
    IReadOnlyList<NotaCreditoRelacion> Relaciones);

public sealed record NotaCreditoRelacion(string TipoRelacion, string UuidRelacionado);

public sealed class NotaCreditoDetalleHandler
    : IRequestHandler<NotaCreditoDetalleQuery, NotaCreditoDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public NotaCreditoDetalleHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<NotaCreditoDetalleResponse> Handle(
        NotaCreditoDetalleQuery query, CancellationToken cancellationToken)
    {
        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var n = await alcance.AplicarA(_db.NotasCredito.AsNoTracking())
            .Include(x => x.Relaciones)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "NOTA_CREDITO_NO_ENCONTRADA", $"No existe la nota de crédito '{query.Id}'.");

        var relaciones = n.Relaciones
            .Select(r => new NotaCreditoRelacion(r.TipoRelacion, r.UuidRelacionado))
            .ToList();

        return new NotaCreditoDetalleResponse(
            n.Id, n.Folio, n.Estado.ToString(), n.Uuid, n.ReceptorNombre, n.ReceptorRfc,
            n.Motivo.ToString(), n.FacturaRelacionadaId, n.AnticipoOrigenId, n.Descripcion,
            n.Subtotal, n.ImpuestosTrasladados, n.Total, n.Moneda, n.FechaTimbrado, n.Version, relaciones);
    }
}
