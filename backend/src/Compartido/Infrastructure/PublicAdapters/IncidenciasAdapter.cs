using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Application.Incidencias;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.Infrastructure.PublicAdapters;

/// <summary>
/// Implementación de <see cref="IIncidenciasPort"/> (U-4) sobre <c>compartido.incidencias</c>. El actor es el usuario
/// actual; sin usuario (workers, eventos) la incidencia queda abierta o resuelta por «Sistema».
/// </summary>
public sealed class IncidenciasAdapter(CompartidoDbContext db, IncidenciasServicio servicio, ICurrentUserContext usuario) : IIncidenciasPort
{
    public async Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
    {
        var (i, ya) = await servicio.AbrirAsync(solicitud, usuario.UserId, cancellationToken);
        return new IncidenciaAbierta(i.Id, i.Numero, ya);
    }

    public async Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken)
    {
        var q = db.Incidencias.Include(x => x.Historial)
            .Where(x => x.Paso == paso && x.DocumentoId == documentoId
                && (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion));
        if (detalleClave is not null)
        {
            var clave = detalleClave.Trim();
            q = q.Where(x => x.DetalleClave == clave);
        }
        var pendientes = await q.ToListAsync(cancellationToken);
        if (pendientes.Count == 0)
            return 0;
        var actor = usuario.UserId;
        var nombre = actor is Guid a ? await servicio.NombreUsuarioAsync(a, cancellationToken) : null;
        foreach (var i in pendientes)
            i.Resolver(actor, nombre, resolucion, servicio.Ahora);
        await db.SaveChangesAsync(cancellationToken);
        return pendientes.Count;
    }

    public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) =>
        db.Incidencias.AsNoTracking().AnyAsync(x => x.DocumentoId == documentoId && (paso == null || x.Paso == paso)
            && (x.Estado == EstadoIncidencia.Abierta || x.Estado == EstadoIncidencia.EnAtencion), cancellationToken);
}
