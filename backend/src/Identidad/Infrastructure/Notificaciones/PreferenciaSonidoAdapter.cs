using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;

namespace Millet.Identidad.Infrastructure.Notificaciones;

public sealed class PreferenciaSonidoAdapter(IdentidadDbContext db)
{
    public Task<bool> ObtenerAsync(Guid usuarioId, CancellationToken ct) =>
        db.UsuarioPreferencias.Where(x => x.UsuarioId == usuarioId).Select(x => x.SonidoAvisosHabilitado).SingleOrDefaultAsync(ct);

    public async Task GuardarAsync(Guid usuarioId, bool habilitado, CancellationToken ct)
    {
        var preferencia = await db.UsuarioPreferencias.SingleOrDefaultAsync(x => x.UsuarioId == usuarioId, ct);
        if (preferencia is null)
        {
            preferencia = new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId);
            db.UsuarioPreferencias.Add(preferencia);
        }
        preferencia.CambiarSonidoAvisos(habilitado);
        await db.SaveChangesAsync(ct);
    }
}
