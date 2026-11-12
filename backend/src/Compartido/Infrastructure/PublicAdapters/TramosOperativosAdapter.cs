using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Tramos;
using Millet.SharedKernel.Domain.Tramos;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>M-303: implementación de <see cref="ITramosOperativosPort"/> sobre <c>compartido.tramos_operativos</c>.</summary>
public sealed class TramosOperativosAdapter(CompartidoDbContext db, IClock clock) : ITramosOperativosPort
{
    public Task<bool> DetenidoAsync(string clave, CancellationToken ct) =>
        db.TramosOperativos.AsNoTracking().AnyAsync(t => t.Clave == clave && t.Detenido, ct);

    /// <summary>Sin pasar por el token de versión: un ciclo del worker no debe chocar con quien detiene el tramo al mismo tiempo.</summary>
    public async Task RegistrarEventoCorrectoAsync(string clave, string detalle, CancellationToken ct)
    {
        var ahora = clock.UtcNow;
        var texto = detalle.Length > 300 ? detalle[..300] : detalle;
        var n = await db.TramosOperativos.Where(t => t.Clave == clave)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UltimoEventoCorrectoAt, ahora).SetProperty(t => t.UltimoEventoCorrecto, texto), ct);
        if (n > 0) return;
        var tramo = new TramoOperativo(clave);
        tramo.RegistrarEventoCorrecto(texto, ahora);
        db.TramosOperativos.Add(tramo);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(tramo).State = EntityState.Detached; } // otro ciclo la creó primero
    }
}
