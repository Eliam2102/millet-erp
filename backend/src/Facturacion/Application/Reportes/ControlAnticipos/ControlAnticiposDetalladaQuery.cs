using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Facturacion.Application.Reportes.ControlAnticipos;

/// <summary>
/// Control de Anticipos — vista Detallada / estado de cuenta por cliente (§6.7,
/// B12, FE-F4): cada anticipo del cliente con sus facturas vinculadas, las NCs de
/// amortización aplicadas (indicador timbrado) y sus saldos.
/// </summary>
public sealed record ControlAnticiposDetalladaQuery(Guid ClienteId) : IRequest<ControlAnticiposDetalladaResponse>;

public sealed record ControlAnticiposDetalladaResponse(
    Guid ClienteId,
    DateTimeOffset GeneradoEn,
    decimal TotalCobrado,
    decimal TotalAmortizado,
    decimal TotalSaldo,
    IReadOnlyList<AnticipoEstadoCuenta> Anticipos);

public sealed record AnticipoEstadoCuenta(
    Guid AnticipoId,
    string Folio,
    string TipoAnticipo,
    decimal MontoCobrado,
    decimal MontoAmortizado,
    decimal Saldo,
    string Estado,
    string? PedidoOrigenRef,
    // 13-A/13-J: enlace al detalle de la factura de anticipo + estado del CFDI
    // (el AnticipoPicker excluye anticipos cuyo CFDI no está Timbrado).
    Guid FacturaAnticipoId,
    string EstadoCfdi,
    IReadOnlyList<AnticipoVinculacionDetalle> Vinculaciones);

public sealed record AnticipoVinculacionDetalle(
    Guid FacturaVentaId,
    string? FacturaFolio,
    decimal Importe,
    Guid? NcAmortizacionId,
    string? NcFolio,
    bool NcTimbrada);

public sealed class ControlAnticiposDetalladaHandler
    : IRequestHandler<ControlAnticiposDetalladaQuery, ControlAnticiposDetalladaResponse>
{
    private readonly FacturacionDbContext _db;
    private readonly IClock _clock;

    public ControlAnticiposDetalladaHandler(FacturacionDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ControlAnticiposDetalladaResponse> Handle(
        ControlAnticiposDetalladaQuery query, CancellationToken cancellationToken)
    {
        var anticipos = await _db.Anticipos.AsNoTracking()
            .Include(a => a.Vinculaciones)
            .Where(a => a.ClienteId == query.ClienteId)
            .ToListAsync(cancellationToken);

        var facturaIds = anticipos.SelectMany(a => a.Vinculaciones.Select(v => v.FacturaVentaId)).Distinct().ToList();
        var ncIds = anticipos.SelectMany(a => a.Vinculaciones.Where(v => v.NcAmortizacionId is not null).Select(v => v.NcAmortizacionId!.Value)).Distinct().ToList();
        var anticipoIds = anticipos.Select(a => a.FacturaAnticipoId).ToList();

        var facturas = await _db.FacturasVenta.AsNoTracking()
            .Where(f => facturaIds.Contains(f.Id)).Select(f => new { f.Id, f.Folio }).ToListAsync(cancellationToken);
        var ncs = await _db.NotasCredito.AsNoTracking()
            .Where(n => ncIds.Contains(n.Id)).Select(n => new { n.Id, n.Folio, n.Estado }).ToListAsync(cancellationToken);
        var folioAnticipos = await _db.FacturasAnticipo.AsNoTracking()
            .Where(f => anticipoIds.Contains(f.Id)).Select(f => new { f.Id, f.Folio, f.Estado }).ToListAsync(cancellationToken);

        var facturaPorId = facturas.ToDictionary(f => f.Id, f => f.Folio);
        var ncPorId = ncs.ToDictionary(n => n.Id, n => n);
        var cfdiAnticipoPorId = folioAnticipos.ToDictionary(f => f.Id, f => f);

        var estados = anticipos.Select(a => new AnticipoEstadoCuenta(
            a.Id,
            cfdiAnticipoPorId.TryGetValue(a.FacturaAnticipoId, out var cfdi) ? cfdi.Folio : "",
            a.TipoAnticipo.ToString(),
            a.MontoCobrado, a.MontoAmortizado, a.Saldo, a.Estado.ToString(), a.PedidoOrigenRef,
            a.FacturaAnticipoId,
            cfdi?.Estado.ToString() ?? "",
            a.Vinculaciones.Select(v =>
            {
                var nc = v.NcAmortizacionId is { } ncId && ncPorId.TryGetValue(ncId, out var n) ? n : null;
                return new AnticipoVinculacionDetalle(
                    v.FacturaVentaId, facturaPorId.GetValueOrDefault(v.FacturaVentaId), v.Importe,
                    v.NcAmortizacionId, nc?.Folio,
                    nc is not null && nc.Estado == Domain.Comprobantes.EstadoTimbrado.Timbrado);
            }).ToList()))
            .ToList();

        return new ControlAnticiposDetalladaResponse(
            query.ClienteId, _clock.UtcNow,
            estados.Sum(e => e.MontoCobrado), estados.Sum(e => e.MontoAmortizado), estados.Sum(e => e.Saldo),
            estados);
    }
}
