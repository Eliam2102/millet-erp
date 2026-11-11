using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.ConciliacionAuxiliares;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Infrastructure.PublicAdapters;

/// <summary>
/// F1-CON-09: total de cartera al corte por moneda, para la conciliación de auxiliares de Contabilidad. CxC guarda el saldo actual;
/// aquí se reconstruye al corte: facturas timbradas hasta el corte menos los pagos y notas de crédito con fecha ≤ corte que no
/// estaban revertidos al corte. Supuesto a validar: la cartera no guarda la fecha de cancelación, así que una factura cancelada
/// no cuenta en ningún corte. Solo totales: el detalle por documento es la conciliación con pólizas de CxC.
/// </summary>
public sealed class SaldoCarteraContabilidadAdapter(CuentasPorCobrarDbContext db) : ISaldoCarteraAlCorteReadPort
{
    public async Task<IReadOnlyList<SaldoCarteraMoneda>> LeerAsync(DateOnly corte, CancellationToken ct)
    {
        var fin = new DateTimeOffset(corte.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var facturas = await db.FacturasCartera.AsNoTracking()
            .Where(f => f.FechaTimbrado < fin && f.Estado != EstadoFacturaCartera.Cancelada)
            .Select(f => new { f.Id, f.Total, f.Moneda }).ToListAsync(ct);
        var movimientos = (await db.MovimientosCartera.AsNoTracking()
                .Where(m => m.FechaMovimiento < fin && (!m.Revertido || m.RevertidoEn == null || m.RevertidoEn >= fin))
                // M-240: la reversa de un ajuste regresa saldo a la factura: cuenta con signo contrario a un pago.
                .Select(m => new
                {
                    m.FacturaCarteraId,
                    Importe = m.Tipo == TipoMovimientoCartera.ReversaAjusteCobro ? -m.Importe : m.Importe,
                    m.FechaMovimiento, m.Revertido, m.RevertidoEn,
                }).ToListAsync(ct))
            .ToLookup(m => m.FacturaCarteraId);
        return facturas
            .Select(f => new
            {
                f.Moneda,
                Saldo = ComparadorAuxiliares.SaldoCarteraAlCorte(f.Total,
                    movimientos[f.Id].Select(m => (m.Importe, m.FechaMovimiento, m.Revertido, m.RevertidoEn)), fin),
            })
            .Where(x => x.Saldo != 0m)
            .GroupBy(x => x.Moneda)
            .Select(g => new SaldoCarteraMoneda(g.Key, g.Sum(x => x.Saldo), g.Count()))
            .OrderBy(s => s.Moneda, StringComparer.Ordinal).ToList();
    }
}
