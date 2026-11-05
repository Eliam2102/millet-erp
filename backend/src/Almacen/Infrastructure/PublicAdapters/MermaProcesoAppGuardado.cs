using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Mermas;
using Millet.Almacen.Domain.Idempotencia;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.Infrastructure.PublicAdapters;

/// <summary>
/// Guarda de forma atómica el desperdicio de mesa o la merma de proceso que informa A+W (registro + evento + <see cref="MensajeApp"/>).
/// El endpoint es anónimo (llave de API): el <c>Bypass()</c> vive aquí con la empresa explícita, mismo precedente que
/// <see cref="SalidaAppAlmacenesGuardado"/>.
/// </summary>
public sealed class MermaProcesoAppGuardado(AlmacenDbContext db, ICurrentEmpresaContext empresa, IIntegrationEventPublisher events)
{
    /// <summary><c>Duplicado</c> = carrera con otro envío del mismo mensajeId (23505): se revierte y el llamador responde DUPLICADO.</summary>
    public async Task<(string Estado, string? Error, bool Duplicado)> GuardarAsync(
        string mensajeId, string tipo, string? referencia, string payload, Guid empresaId, MermaApp m, CancellationToken ct)
    {
        using var bypass = empresa.Bypass();
        await using var tx = db.Database.IsNpgsql() ? await db.Database.BeginTransactionAsync(ct) : null;

        string estado; string? error;
        try { (estado, error) = await MermaProcesoApp.RegistrarAsync(db, events, empresaId, m, mensajeId, ct); }
        catch (BusinessRuleException ex) { (estado, error) = ("ERROR", ex.Message); }

        db.MensajesApp.Add(new MensajeApp(mensajeId, tipo, referencia?.Length > 100 ? referencia[..100] : referencia, payload, estado, null, error));
        try
        {
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return (estado, error, false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            // Misma llave de mensaje = otro envío ganó la carrera; si no, chocó el índice del desperdicio (mesa y día ya informados).
            return await db.MensajesApp.AnyAsync(x => x.MensajeId == mensajeId, ct)
                ? (estado, error, true)
                : ("ERROR", "Ya hay desperdicio de esa mesa, ese día y ese artículo.", false);
        }
    }
}
