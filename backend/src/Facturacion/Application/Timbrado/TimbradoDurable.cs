using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Timbrado;

/// <summary>
/// M-173 (86e3zr6wk): la solicitud de timbrado queda guardada ANTES de llamar al PAC. El comprobante
/// (con su folio reservado) y lo que toma (pedido, salida, autorización) se guardan en
/// <see cref="EstadoTimbrado.TimbradoEnProceso"/>; si el proceso o la red caen durante la llamada,
/// el comprobante sigue en la bandeja y el <c>TimbradoPendienteWorker</c> lo resuelve. Si el PAC
/// responde, el guardado siguiente del handler aplica el resultado como siempre.
/// </summary>
public static class TimbradoDurable
{
    /// <summary>
    /// Callback para <see cref="TimbradoEjecutor.TimbrarYAplicarAsync"/> en una emisión: agrega el
    /// comprobante si aún no está en el contexto y guarda. <paramref name="alGuardar"/> avisa al handler
    /// que el comprobante ya existe en la base (no se vuelve a agregar ni se descarta su folio).
    /// <para>
    /// Si la emisión corre dentro de una transacción (REP, FAC-03: bloqueo por factura), con
    /// <paramref name="confirmarTransaccion"/> se confirma aquí: un guardado sin confirmar no sobrevive a una
    /// caída. Lo que sigue se guarda fuera de la transacción; quien la abrió no la vuelve a confirmar
    /// (ver <see cref="TransaccionAbierta"/>).
    /// </para>
    /// </summary>
    public static Func<CancellationToken, Task> AntesDelPac(
        FacturacionDbContext db, Comprobante comprobante, Action? alGuardar = null, bool confirmarTransaccion = false) => async ct =>
    {
        if (db.Entry(comprobante).State == EntityState.Detached)
            db.Add(comprobante);
        await GuardarAsync(db,
            "EMISION_CONCURRENTE",
            "Otro usuario modificó este documento o su origen mientras se emitía. Recarga y revisa antes de volver a intentarlo.", ct);
        if (confirmarTransaccion && db.Database.CurrentTransaction is { } tx)
            await tx.CommitAsync(ct);
        alGuardar?.Invoke();
    };

    /// <summary>True si <paramref name="tx"/> sigue siendo la transacción en curso (no se confirmó antes del PAC).</summary>
    public static bool TransaccionAbierta(FacturacionDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx) =>
        tx is not null && db.Database.CurrentTransaction == tx;

    /// <summary>
    /// Guarda; un conflicto de concurrencia (token <c>Version</c>, ADR-0012) se traduce a
    /// <see cref="ConflictException"/> sin llamar al PAC.
    /// </summary>
    public static async Task GuardarAsync(FacturacionDbContext db, string codigo, string mensaje, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException(codigo, mensaje, ex);
        }
    }
}
