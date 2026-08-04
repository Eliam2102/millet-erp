using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Repp;

// ============================================================================
// TES-PR8 (§3.6.b): read model de pagos a proveedor ejecutados sin REPP
// recibido, con antigüedad y SLA de 5 días (alineado al motivo FALTA_REPP
// de CxP). Reemplaza la consulta SQL semanal + Excel vs "One Factor".
//
// MetodoPago viene de la proyección del pasivo (extensión aditiva del
// evento de CxP, T-G11 decisión (a)): por default se listan PPD y
// "sin dato" (pasivos previos a la extensión o sin CFDI ligado) — solo
// PPD exige REPP, pero un null oculto sería un falso negativo. PUE
// explícito queda fuera.
// ============================================================================

public sealed record ReppPendienteResponse(
    Guid FacturaProveedorId,
    Guid ProveedorId,
    string? ProveedorClave,
    string? ProveedorRazonSocial,
    string? FolioProveedor,
    Guid? UuidCfdi,
    string? MetodoPago,
    decimal MontoPagado,
    string Moneda,
    DateOnly FechaPrimerPago,
    int DiasSinRepp,
    bool VencidoSla);

public sealed record ReppPendientesQuery(
    Guid? ProveedorId = null,
    bool SoloVencidos = false,
    bool IncluirSinMetodo = true,
    int Offset = 0,
    int Limit = 50) : IRequest<PagedResponse<ReppPendienteResponse>>;

public sealed class ReppPendientesHandler
    : IRequestHandler<ReppPendientesQuery, PagedResponse<ReppPendienteResponse>>
{
    /// <summary>SLA de recepción del complemento (§3.6.b; seed FALTA_REPP de CxP).</summary>
    public const int SlaDias = 5;

    private readonly TesoreriaDbContext _db;
    private readonly IProveedorBancoReadPort _proveedores;
    private readonly IClock _clock;

    public ReppPendientesHandler(
        TesoreriaDbContext db, IProveedorBancoReadPort proveedores, IClock clock)
    {
        _db = db; _proveedores = proveedores; _clock = clock;
    }

    public async Task<PagedResponse<ReppPendienteResponse>> Handle(
        ReppPendientesQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        // Pagos vigentes agregados por factura (fecha del hecho bancario =
        // FechaValor del movimiento), sin REPP registrado.
        var pagosPorFactura = _db.AplicacionesPagoProveedor.AsNoTracking()
            .Where(a => !a.Revertida)
            .Join(_db.MovimientosBancarios.AsNoTracking(),
                a => a.MovimientoId, m => m.Id,
                (a, m) => new { a.FacturaProveedorId, a.ImporteAplicado, m.FechaValor, m.Moneda })
            .GroupBy(x => new { x.FacturaProveedorId, x.Moneda })
            .Select(g => new
            {
                g.Key.FacturaProveedorId,
                g.Key.Moneda,
                MontoPagado = g.Sum(x => x.ImporteAplicado),
                FechaPrimerPago = g.Min(x => x.FechaValor),
            });

        var q = pagosPorFactura
            .Where(p => !_db.ReppsProveedorRecibidos.Any(r => r.FacturaProveedorId == p.FacturaProveedorId))
            .Join(_db.PasivosPendientesPago.AsNoTracking(),
                p => p.FacturaProveedorId, pas => pas.FacturaProveedorId,
                (p, pas) => new
                {
                    p.FacturaProveedorId,
                    pas.ProveedorId,
                    pas.FolioProveedor,
                    pas.UuidCfdi,
                    pas.MetodoPago,
                    p.MontoPagado,
                    p.Moneda,
                    p.FechaPrimerPago,
                });

        // Solo PPD exige REPP; null = sin dato (visible por default para
        // no ocultar candidatos). PUE explícito nunca entra.
        q = query.IncluirSinMetodo
            ? q.Where(x => x.MetodoPago == "PPD" || x.MetodoPago == null)
            : q.Where(x => x.MetodoPago == "PPD");

        if (query.ProveedorId is Guid proveedor) q = q.Where(x => x.ProveedorId == proveedor);
        if (query.SoloVencidos)
        {
            var corte = hoy.AddDays(-SlaDias);
            q = q.Where(x => x.FechaPrimerPago < corte);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(x => x.FechaPrimerPago)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var proveedorIds = items.Select(x => x.ProveedorId).Distinct().ToList();
        var proveedores = await _proveedores.ObtenerVariosAsync(proveedorIds, cancellationToken);

        var responses = items.Select(x =>
        {
            proveedores.TryGetValue(x.ProveedorId, out var prov);
            var dias = hoy.DayNumber - x.FechaPrimerPago.DayNumber;
            return new ReppPendienteResponse(
                FacturaProveedorId: x.FacturaProveedorId,
                ProveedorId: x.ProveedorId,
                ProveedorClave: prov?.Clave,
                ProveedorRazonSocial: prov?.RazonSocial,
                FolioProveedor: x.FolioProveedor,
                UuidCfdi: x.UuidCfdi,
                MetodoPago: x.MetodoPago,
                MontoPagado: x.MontoPagado,
                Moneda: x.Moneda,
                FechaPrimerPago: x.FechaPrimerPago,
                DiasSinRepp: dias,
                VencidoSla: dias > SlaDias);
        }).ToList();

        return new PagedResponse<ReppPendienteResponse>(responses, offset, limit, total);
    }
}
