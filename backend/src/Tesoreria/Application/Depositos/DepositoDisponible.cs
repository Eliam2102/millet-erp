using Microsoft.EntityFrameworkCore;
using Millet.Tesoreria.Domain.Depositos;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Depositos;

/// <summary>
/// C7.4 · sobrante: cuánto falta por aplicar de un ingreso bancario. Sin columna
/// nueva (sin migración): lo aplicado se deriva de las confirmaciones de depósito
/// que ya ligaron el movimiento. Un movimiento <c>Aplicado</c> no tiene
/// disponible; uno <c>NoAplicado</c> tiene su monto completo; uno
/// <c>AplicadoParcial</c> tiene su monto menos lo confirmado (el saldo a favor
/// por identificar de la propuesta de CxC queda disponible para identificarse).
/// </summary>
internal static class DepositoDisponible
{
    public static async Task<IReadOnlyDictionary<Guid, decimal>> AplicadoPorMovimientoAsync(
        TesoreriaDbContext db, IReadOnlyCollection<MovimientoBancario> movimientos, CancellationToken ct)
    {
        if (movimientos.Count == 0) return new Dictionary<Guid, decimal>();
        var ids = movimientos.Select(m => m.Id).ToArray();
        var montos = movimientos.ToDictionary(m => m.Id, m => m.Monto);
        var confirmadas = await db.DepositosConfirmacion.AsNoTracking()
            .Where(d => d.Estado == EstadoDepositoConfirmacion.Confirmada && d.MovimientoId != null && ids.Contains(d.MovimientoId.Value))
            .ToListAsync(ct);
        return confirmadas
            .GroupBy(d => d.MovimientoId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.ImporteAplicadoDe(montos[g.Key])));
    }

    public static decimal Disponible(MovimientoBancario m, decimal aplicado) => m.EstadoAplicacion switch
    {
        EstadoAplicacionMovimiento.Aplicado => 0m,
        EstadoAplicacionMovimiento.NoAplicado => m.Monto,
        _ => Math.Max(0m, m.Monto - aplicado),
    };
}
