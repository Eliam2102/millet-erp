using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Cierre;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Infrastructure.PublicAdapters;

/// <summary>
/// CON-12 (86e3jvvme): cuentas bancarias cuyo mes no está conciliado (T9.2). Exige conciliación cerrada a las cuentas con movimientos
/// en el mes o con una conciliación del mes ya iniciada. Supuesto a validar: una cuenta sin movimientos en el mes no exige conciliación.
/// Los movimientos sin conciliar se informan junto a la cuenta. Solo lectura.
/// </summary>
public sealed class ConciliacionBancariaCierreAdapter(TesoreriaDbContext db) : IConciliacionBancariaCierreReadPort
{
    public async Task<IReadOnlyList<CuentaBancariaSinConciliar>> PendientesAsync(int anio, int mes, CancellationToken ct)
    {
        var desde = new DateOnly(anio, mes, 1);
        var hasta = desde.AddMonths(1).AddDays(-1);
        var movimientos = await db.MovimientosBancarios.AsNoTracking().Where(m => m.FechaValor >= desde && m.FechaValor <= hasta)
            .GroupBy(m => m.CuentaBancariaId)
            .Select(g => new { g.Key, Total = g.Count(), SinConciliar = g.Count(m => m.EstadoConciliacion == EstadoConciliacionMovimiento.NoConciliado) })
            .ToDictionaryAsync(x => x.Key, ct);
        var conciliaciones = await db.Conciliaciones.AsNoTracking().Where(c => c.PeriodoAnio == anio && c.PeriodoMes == mes)
            .Select(c => new { c.CuentaBancariaId, c.Estado }).ToListAsync(ct);
        var ids = movimientos.Keys.Concat(conciliaciones.Select(c => c.CuentaBancariaId)).Distinct().ToList();
        if (ids.Count == 0) return [];
        var cuentas = await db.CuentasBancarias.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        var pendientes = new List<CuentaBancariaSinConciliar>();
        foreach (var id in ids)
        {
            var estados = conciliaciones.Where(c => c.CuentaBancariaId == id).Select(c => c.Estado).ToList();
            if (estados.Contains(EstadoConciliacion.Cerrada)) continue;
            var sinConciliar = movimientos.TryGetValue(id, out var m) ? m.SinConciliar : 0;
            var cuenta = cuentas.TryGetValue(id, out var c) ? $"{c.Banco} {c.NumeroCuenta} ({c.Moneda})" : id.ToString();
            pendientes.Add(new(id, cuenta, estados.Count == 0 ? "sin conciliación del mes" : "conciliación abierta", sinConciliar));
        }
        return [.. pendientes.OrderBy(p => p.Cuenta)];
    }
}
