using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.Application.Pagos;

// F1-TES-05 brecha 12 (plano 1.7.1 Tesorería §7.3 paso 3, §12 «Reenvío»): la referencia bancaria
// (folio del banco) respalda un solo egreso por cuenta propia. Un reintento idéntico recupera el
// resultado previo; cualquier otro uso de la misma referencia se rechaza.
internal static class ReferenciaPagoUnica
{
    /// <summary>Egreso vigente (no contramovimiento) de la cuenta con esa referencia, o null.</summary>
    public static async Task<MovimientoBancario?> BuscarAsync(
        TesoreriaDbContext db, Guid cuentaBancariaId, string? referencia, CancellationToken ct)
    {
        var r = MovimientoBancario.NormalizarReferencia(referencia);
        if (r is null) return null;
        return await db.MovimientosBancarios.FirstOrDefaultAsync(m =>
            m.CuentaBancariaId == cuentaBancariaId
            && m.Sentido == SentidoMovimiento.Egreso
            && m.ReferenciaBancaria == r
            && m.ContramovimientoDe == null, ct);
    }

    public static BusinessRuleException Duplicada(MovimientoBancario existente) =>
        new("PAGO_REFERENCIA_DUPLICADA",
            $"La referencia bancaria {existente.ReferenciaBancaria} ya respalda el pago {existente.Id} de esta cuenta. "
            + "Si es el mismo pago, ya quedó registrado; si es otro, captura el folio que dio el banco.");

    public static async Task<bool> RevertidoAsync(TesoreriaDbContext db, Guid movimientoId, CancellationToken ct) =>
        await db.MovimientosBancarios.AnyAsync(m => m.ContramovimientoDe == movimientoId, ct);
}
