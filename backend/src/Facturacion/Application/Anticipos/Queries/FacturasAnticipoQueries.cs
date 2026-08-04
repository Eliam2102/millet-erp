using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Alcance;
using Millet.Facturacion.Application.Facturas.Queries;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.Queries;

// ---- Bandeja (CAJAS-PR2; saldo del anticipo 13-H) ----

/// <summary>
/// Bandeja de facturas de anticipo (CAJAS-PR2 — no existía; entra con la Capa
/// A para que la familia quede filtrada desde su primera lectura).
/// <paramref name="SoloSinAsignar"/> restringe al bucket "Sin asignar"
/// (requiere <c>facturacion.caja.leer-todas</c>). 13-H: cada renglón lleva el
/// estado y saldo del agregado <c>Anticipo</c> asociado.
/// </summary>
public sealed record BandejaFacturasAnticipoQuery(
    EstadoTimbrado? Estado, int Offset, int Limit, bool SoloSinAsignar = false)
    : IRequest<BandejaFacturasAnticipoResponse>;

public sealed record BandejaFacturasAnticipoResponse(
    IReadOnlyList<FacturaAnticipoBandejaItem> Items,
    int? SinAsignarCount);

public sealed record FacturaAnticipoBandejaItem(
    Guid Id, string Folio, string Estado, string? Uuid, string ReceptorNombre, string ReceptorRfc,
    string TipoAnticipo, decimal Total, string Moneda, DateTimeOffset? FechaTimbrado,
    string? EstadoAnticipo, decimal? Saldo);

public sealed class BandejaFacturasAnticipoHandler
    : IRequestHandler<BandejaFacturasAnticipoQuery, BandejaFacturasAnticipoResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public BandejaFacturasAnticipoHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<BandejaFacturasAnticipoResponse> Handle(
        BandejaFacturasAnticipoQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 50 : query.Limit, 1, 200);
        var q = _db.FacturasAnticipo.AsNoTracking();
        if (query.Estado is { } estado) q = q.Where(f => f.Estado == estado);

        (q, var sinAsignarCount) = await AlcanceBandejaHelper.AplicarAsync(
            _alcance, q, query.SoloSinAsignar, cancellationToken);

        var rows = await q
            .OrderByDescending(f => f.FolioNumero)
            .Skip(Math.Max(0, query.Offset)).Take(limit)
            .Select(f => new
            {
                f.Id, f.Folio, f.Estado, f.Uuid, f.ReceptorNombre, f.ReceptorRfc,
                f.TipoAnticipo, f.Total, f.Moneda, f.FechaTimbrado, f.AnticipoId
            })
            .ToListAsync(cancellationToken);

        // 13-H: saldo del agregado Anticipo (relación 1:1 por id, misma TX de emisión).
        var anticipoIds = rows.Select(r => r.AnticipoId).Distinct().ToList();
        var saldos = anticipoIds.Count == 0
            ? []
            : await _db.Anticipos.AsNoTracking()
                .Where(a => anticipoIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Estado, a.Saldo })
                .ToListAsync(cancellationToken);
        var saldoPorId = saldos.ToDictionary(a => a.Id, a => a);

        var items = rows.Select(r =>
        {
            saldoPorId.TryGetValue(r.AnticipoId, out var a);
            return new FacturaAnticipoBandejaItem(
                r.Id, r.Folio, r.Estado.ToString(), r.Uuid, r.ReceptorNombre, r.ReceptorRfc,
                r.TipoAnticipo.ToString(), r.Total, r.Moneda, r.FechaTimbrado,
                a?.Estado.ToString(), a?.Saldo);
        }).ToList();

        return new BandejaFacturasAnticipoResponse(items, sinAsignarCount);
    }
}

// ---- Detalle (CAJAS-PR2; enriquecido 13-A) ----

/// <summary>Detalle de una factura de anticipo. Fuera de alcance → 404.</summary>
public sealed record FacturaAnticipoDetalleQuery(Guid Id) : IRequest<FacturaAnticipoDetalleResponse>;

public sealed record FacturaAnticipoDetalleResponse(
    Guid Id, string Folio, string Estado, string? Uuid, string ReceptorNombre, string ReceptorRfc,
    string TipoAnticipo, Guid AnticipoId, Guid? PedidoFacturableId, string Descripcion,
    decimal Subtotal, decimal ImpuestosTrasladados, decimal Total, string Moneda,
    DateTimeOffset? FechaTimbrado, int Version,
    // 13-A: error del último intento de timbrado (banner #530) + cadena de
    // relaciones CFDI + saldo/vinculaciones del agregado Anticipo.
    string? TimbradoErrorCodigo,
    string? TimbradoErrorMensaje,
    IReadOnlyList<RelacionCfdiDetalle> Relaciones,
    AnticipoSaldoDetalle? Anticipo);

/// <summary>Saldo amortizable del anticipo (agregado <c>Anticipo</c>, 13-A).</summary>
public sealed record AnticipoSaldoDetalle(
    Guid AnticipoId, Guid ClienteId, string Estado,
    decimal MontoCobrado, decimal MontoAmortizado, decimal Saldo, decimal SaldoDisponible,
    string? PedidoOrigenRef, long? ObraId, string? ObraNombre,
    IReadOnlyList<AnticipoVinculacionInfo> Vinculaciones);

/// <summary>Vinculación M2/M3: factura final + NC de amortización (si ya existe).</summary>
public sealed record AnticipoVinculacionInfo(
    Guid FacturaVentaId, string? FacturaFolio, string? FacturaUuid, string? FacturaEstado,
    decimal Importe, DateTimeOffset CreadoEn,
    Guid? NcAmortizacionId, string? NcFolio, string? NcUuid, string? NcEstado);

public sealed class FacturaAnticipoDetalleHandler
    : IRequestHandler<FacturaAnticipoDetalleQuery, FacturaAnticipoDetalleResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IAlcanceCajaEvaluator _alcance;

    public FacturaAnticipoDetalleHandler(FacturacionDbContext db, IAlcanceCajaEvaluator alcance)
    {
        _db = db;
        _alcance = alcance;
    }

    public async Task<FacturaAnticipoDetalleResponse> Handle(
        FacturaAnticipoDetalleQuery query, CancellationToken cancellationToken)
    {
        // Fuera de alcance → mismo 404 que inexistente (12-cajas.md §4.1).
        var alcance = await _alcance.ResolverAsync(cancellationToken);
        var f = await alcance.AplicarA(_db.FacturasAnticipo.AsNoTracking())
            .Include(x => x.Relaciones)
            .FirstOrDefaultAsync(x => x.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "FACTURA_ANTICIPO_NO_ENCONTRADA", $"No existe la factura de anticipo '{query.Id}'.");

        // Enriquecer relaciones CFDI por UUID (mismo patrón que ComprobanteDetalleQuery).
        var uuids = f.Relaciones.Select(r => r.UuidRelacionado).Distinct().ToList();
        var relacionados = uuids.Count == 0
            ? []
            : await _db.Comprobantes.AsNoTracking()
                .Where(c => c.Uuid != null && uuids.Contains(c.Uuid))
                .Select(c => new { c.Uuid, c.Folio, c.Tipo, c.Total, c.FechaTimbrado })
                .ToListAsync(cancellationToken);
        var porUuid = relacionados.ToDictionary(c => c.Uuid!, c => c);
        var relaciones = f.Relaciones
            .Select(r =>
            {
                porUuid.TryGetValue(r.UuidRelacionado, out var c);
                return new RelacionCfdiDetalle(
                    r.TipoRelacion, r.UuidRelacionado, c?.Folio, c?.Tipo.ToString(), c?.Total, c?.FechaTimbrado);
            })
            .ToList();

        var anticipo = await CargarSaldoAsync(f.AnticipoId, cancellationToken);

        return new FacturaAnticipoDetalleResponse(
            f.Id, f.Folio, f.Estado.ToString(), f.Uuid, f.ReceptorNombre, f.ReceptorRfc,
            f.TipoAnticipo.ToString(), f.AnticipoId, f.PedidoFacturableId, f.Descripcion,
            f.Subtotal, f.ImpuestosTrasladados, f.Total, f.Moneda, f.FechaTimbrado, f.Version,
            f.TimbradoErrorCodigo, f.TimbradoErrorMensaje, relaciones, anticipo);
    }

    private async Task<AnticipoSaldoDetalle?> CargarSaldoAsync(Guid anticipoId, CancellationToken cancellationToken)
    {
        var a = await _db.Anticipos.AsNoTracking()
            .Include(x => x.Vinculaciones)
            .FirstOrDefaultAsync(x => x.Id == anticipoId, cancellationToken);
        if (a is null)
            return null; // inconsistencia de datos: el CFDI existe sin saldo.

        var facturaIds = a.Vinculaciones.Select(v => v.FacturaVentaId).Distinct().ToList();
        var ncIds = a.Vinculaciones
            .Where(v => v.NcAmortizacionId is not null)
            .Select(v => v.NcAmortizacionId!.Value).Distinct().ToList();

        var facturas = facturaIds.Count == 0
            ? []
            : await _db.FacturasVenta.AsNoTracking()
                .Where(x => facturaIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Folio, x.Uuid, x.Estado })
                .ToListAsync(cancellationToken);
        var ncs = ncIds.Count == 0
            ? []
            : await _db.NotasCredito.AsNoTracking()
                .Where(x => ncIds.Contains(x.Id))
                .Select(x => new { x.Id, x.Folio, x.Uuid, x.Estado })
                .ToListAsync(cancellationToken);
        var facturaPorId = facturas.ToDictionary(x => x.Id, x => x);
        var ncPorId = ncs.ToDictionary(x => x.Id, x => x);

        var vinculaciones = a.Vinculaciones
            .OrderBy(v => v.CreadoEn)
            .Select(v =>
            {
                facturaPorId.TryGetValue(v.FacturaVentaId, out var fv);
                var nc = v.NcAmortizacionId is { } ncId && ncPorId.TryGetValue(ncId, out var n) ? n : null;
                return new AnticipoVinculacionInfo(
                    v.FacturaVentaId, fv?.Folio, fv?.Uuid, fv?.Estado.ToString(),
                    v.Importe, v.CreadoEn,
                    v.NcAmortizacionId, nc?.Folio, nc?.Uuid, nc?.Estado.ToString());
            })
            .ToList();

        return new AnticipoSaldoDetalle(
            a.Id, a.ClienteId, a.Estado.ToString(),
            a.MontoCobrado, a.MontoAmortizado, a.Saldo, a.SaldoDisponible,
            a.PedidoOrigenRef, a.ObraId, a.ObraNombre, vinculaciones);
    }
}
