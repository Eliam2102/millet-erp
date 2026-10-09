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
    bool VencidoSla, Guid PagoId, decimal ImportePendiente);

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
    private readonly Millet.SharedKernel.Application.Calendario.ICalendarioHabil _calendario;

    public ReppPendientesHandler(
        TesoreriaDbContext db, IProveedorBancoReadPort proveedores, IClock clock, Millet.SharedKernel.Application.Calendario.ICalendarioHabil calendario)
    {
        _db = db; _proveedores = proveedores; _clock = clock; _calendario = calendario;
    }

    public async Task<PagedResponse<ReppPendienteResponse>> Handle(
        ReppPendientesQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 500);
        var offset = Math.Max(0, query.Offset);
        var hoy = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);

        var q = _db.AplicacionesPagoProveedor.AsNoTracking().Where(a => !a.Revertida)
            .Join(_db.MovimientosBancarios.AsNoTracking(), a => a.MovimientoId, m => m.Id, (a, m) => new { a, m })
            .Join(_db.PasivosPendientesPago.AsNoTracking(), x => x.a.FacturaProveedorId, p => p.FacturaProveedorId,
                (x, p) => new { PagoId = x.a.Id, x.a.FacturaProveedorId, p.ProveedorId, p.FolioProveedor, p.UuidCfdi, p.MetodoPago,
                    MontoPagado = x.a.ImporteAplicado, x.m.Moneda, FechaPrimerPago = x.m.FechaValor,
                    Cubierto = _db.ReppPagosProveedor.Where(r => r.PagoId == x.a.Id).Sum(r => (decimal?)r.Importe) ?? 0 });
        q = query.IncluirSinMetodo ? q.Where(x => x.MetodoPago == "PPD" || x.MetodoPago == null) : q.Where(x => x.MetodoPago == "PPD");
        q = q.Where(x => x.MontoPagado > x.Cubierto);
        if (query.ProveedorId is Guid proveedor) q = q.Where(x => x.ProveedorId == proveedor);
        var items = await q.OrderBy(x => x.FechaPrimerPago).ThenBy(x => x.PagoId).ToListAsync(cancellationToken);
        var proveedores = await _proveedores.ObtenerVariosAsync(items.Select(x => x.ProveedorId).Distinct().ToArray(), cancellationToken);
        var responses = new List<ReppPendienteResponse>();
        foreach (var x in items)
        {
            var dias = await _calendario.ContarDiasAsync(x.FechaPrimerPago, hoy, cancellationToken);
            if (query.SoloVencidos && dias <= SlaDias) continue;
            proveedores.TryGetValue(x.ProveedorId, out var prov);
            responses.Add(new(x.FacturaProveedorId, x.ProveedorId, prov?.Clave, prov?.RazonSocial, x.FolioProveedor,
                x.UuidCfdi, x.MetodoPago, x.MontoPagado, x.Moneda, x.FechaPrimerPago, dias, dias > SlaDias, x.PagoId, x.MontoPagado - x.Cubierto));
        }
        var total = responses.Count;
        responses = responses.Skip(offset).Take(limit).ToList();
        return new PagedResponse<ReppPendienteResponse>(responses, offset, limit, total);
    }
}
