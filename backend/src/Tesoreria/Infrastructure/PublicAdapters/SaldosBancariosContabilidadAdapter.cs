using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.ConciliacionAuxiliares;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Infrastructure.PublicAdapters;

/// <summary>
/// F1-CON-09: saldo en libros de cada cuenta bancaria al corte para la conciliación de auxiliares de Contabilidad. Mismo cálculo que
/// el auxiliar de bancos: saldo inicial más el neto de movimientos con fecha valor ≤ corte.
/// </summary>
public sealed class SaldosBancariosContabilidadAdapter(TesoreriaDbContext db) : ISaldosBancariosAlCorteReadPort
{
    public async Task<IReadOnlyList<SaldoBancoAlCorte>> LeerAsync(DateOnly corte, CancellationToken ct)
    {
        var cuentas = await db.CuentasBancarias.AsNoTracking().ToListAsync(ct);
        var netos = await db.MovimientosBancarios.AsNoTracking().Where(m => m.FechaValor <= corte)
            .GroupBy(m => m.CuentaBancariaId)
            .Select(g => new { g.Key, Neto = g.Sum(m => m.Sentido == SentidoMovimiento.Ingreso ? m.Monto : -m.Monto) })
            .ToDictionaryAsync(x => x.Key, x => x.Neto, ct);
        return cuentas.Select(c => new SaldoBancoAlCorte(c.Id, c.Banco, c.NumeroCuenta, c.Moneda, c.CuentaContableRef,
            ComparadorAuxiliares.SaldoLibrosAlCorte(c.SaldoInicial, c.FechaCorteSaldoInicial, corte, netos.GetValueOrDefault(c.Id)))).ToList();
    }
}
