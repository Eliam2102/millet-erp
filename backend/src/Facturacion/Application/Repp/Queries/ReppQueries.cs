using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Repp.Queries;

// ---- Bandeja (B9) ----

/// <summary>
/// Bandeja de REPP (B9, FE-F6): complementos de pago con sus totales.
/// CAJAS-PR2: filtrada por la Capa A de Cajas; <paramref name="SoloSinAsignar"/>
/// restringe al bucket "Sin asignar" (requiere <c>facturacion.caja.leer-todas</c>).
/// </summary>
public sealed record BandejaReppQuery(
    EstadoTimbrado? Estado, int Offset, int Limit, bool SoloSinAsignar = false) : IRequest<BandejaReppResponse>;

public sealed record BandejaReppResponse(
    IReadOnlyList<ReppBandejaItem> Items,
    int? SinAsignarCount);

public sealed record ReppBandejaItem(
    Guid Id, string Folio, string Estado, string? Uuid, string ReceptorNombre, decimal ImporteTotalPago, DateTimeOffset FechaPago);

public sealed class BandejaReppHandler : IRequestHandler<BandejaReppQuery, BandejaReppResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public BandejaReppHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<BandejaReppResponse> Handle(BandejaReppQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var q = _db.RecibosPago.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(r => r.Estado == estado);

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var rows = await q
            .OrderByDescending(r => r.FolioNumero)
            .Skip(Math.Max(0, query.Offset)).Take(limit)
            .Select(r => new { r.Id, r.Folio, r.Estado, r.Uuid, r.ReceptorNombre, r.ImporteTotalPago, r.FechaPago })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new ReppBandejaItem(
            r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.ReceptorNombre, r.ImporteTotalPago, r.FechaPago)).ToList();

        return new BandejaReppResponse(items, sinAsignarCount);
    }
}

// ---- Detalle (B9) ----

/// <summary>Detalle de un REPP con sus facturas cubiertas (B9).</summary>
public sealed record ReppDetalleQuery(Guid Id) : IRequest<ReppDetalleResponse>;

public sealed record ReppDetalleResponse(
    Guid Id, string Folio, string Estado, string? Uuid, string ReceptorNombre, string MonedaPago,
    decimal ImporteTotalPago, DateTimeOffset FechaPago, IReadOnlyList<ReppFacturaCubierta> FacturasCubiertas,
    // Error del último intento de timbrado + folio del PAC (PR-B reintento).
    string? TimbradoErrorCodigo = null,
    string? TimbradoErrorMensaje = null,
    string? FolioPac = null);

public sealed record ReppFacturaCubierta(
    Guid FacturaVentaId, string? Folio, string FacturaUuid, int NumParcialidad,
    decimal ImportePagado, decimal SaldoInsoluto, decimal GananciaPerdidaCambiaria);

public sealed class ReppDetalleHandler : IRequestHandler<ReppDetalleQuery, ReppDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public ReppDetalleHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<ReppDetalleResponse> Handle(ReppDetalleQuery query, CancellationToken cancellationToken)
    {
        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var r = await alcance.AplicarA(_db.RecibosPago.AsNoTracking())
            .Include(x => x.FacturasPagadas)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException("REPP_NO_ENCONTRADO", $"No existe el REPP '{query.Id}'.");

        var facturaIds = r.FacturasPagadas.Select(f => f.FacturaVentaId).Distinct().ToList();
        var folios = await _db.FacturasVenta.AsNoTracking()
            .Where(f => facturaIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Folio })
            .ToListAsync(cancellationToken);
        var porId = folios.ToDictionary(f => f.Id, f => f.Folio);

        var cubiertas = r.FacturasPagadas
            .Select(f => new ReppFacturaCubierta(
                f.FacturaVentaId, porId.GetValueOrDefault(f.FacturaVentaId), f.FacturaUuid, f.NumParcialidad,
                f.ImportePagado, f.SaldoInsoluto, f.GananciaPerdidaCambiaria))
            .ToList();

        return new ReppDetalleResponse(
            r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.ReceptorNombre, r.MonedaPago,
            r.ImporteTotalPago, r.FechaPago, cubiertas,
            r.TimbradoErrorCodigo, r.TimbradoErrorMensaje, r.FolioPac);
    }
}
