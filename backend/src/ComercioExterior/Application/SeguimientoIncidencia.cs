using MediatR;
using Millet.ComercioExterior.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.Application;
public sealed record SeguimientoIncidenciaDatos(DateOnly FechaCompromiso, string SiguienteAccion, string Motivo);
public sealed record EditarSeguimientoIncidenciaCommand(Guid ExpedienteId, Guid IncidenciaId, int Version, SeguimientoIncidenciaDatos Datos) : IRequest<ExpedienteDetalle>;
public sealed class EditarSeguimientoIncidenciaHandler(ComercioExteriorDbContext db, ExpedienteServicio servicio, IClock clock) : IRequestHandler<EditarSeguimientoIncidenciaCommand, ExpedienteDetalle>
{
    public async Task<ExpedienteDetalle> Handle(EditarSeguimientoIncidenciaCommand request, CancellationToken cancellationToken)
    {
        await servicio.PermisoAsync(PermisosCanonicos.ComercioExteriorIncidenciasGestionar, cancellationToken);
        var e = await servicio.CargarAsync(request.ExpedienteId, true, cancellationToken);
        if (e.Version != request.Version) throw new ConcurrencyException("Expediente", e.Id);
        e.EditarSeguimientoIncidencia(request.IncidenciaId, request.Datos.FechaCompromiso, request.Datos.SiguienteAccion, request.Datos.Motivo, servicio.Actor, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken); return await servicio.DetalleAsync(e, cancellationToken);
    }
}
