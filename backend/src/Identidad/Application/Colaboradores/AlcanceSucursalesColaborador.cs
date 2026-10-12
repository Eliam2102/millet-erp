using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.UsuarioSucursales;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.Colaboradores;

internal static class AlcanceSucursalesColaborador
{
    public static async Task AsignarAsync(Guid? sucursalId, Guid empresaId, Guid usuarioId,
        CompartidoDbContext compartido, IdentidadDbContext identidad, IMediator mediator, CancellationToken ct)
    {
        if (sucursalId is null)
        {
            var alcance = await identidad.UsuarioEmpresaAlcances.SingleOrDefaultAsync(a => a.UsuarioId == usuarioId && a.EmpresaId == empresaId, ct);
            if (alcance is null) identidad.UsuarioEmpresaAlcances.Add(new Millet.Identidad.Domain.UsuarioEmpresaAlcance(Guid.CreateVersion7(), usuarioId, empresaId, true));
            else alcance.Cambiar(true);
            foreach (var asignacion in await identidad.UsuarioSucursales.Where(a => a.UsuarioId == usuarioId && a.EmpresaId == empresaId).ToListAsync(ct))
                asignacion.Desactivar();
            await identidad.SaveChangesAsync(ct);
            return;
        }
        var sucursales = sucursalId is Guid sid ? new List<Guid> { sid }
            : await compartido.Sucursales.AsNoTracking()
                .Where(s => s.EmpresaId == empresaId && s.Estatus == EstatusCatalogo.Activo)
                .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in sucursales)
        {
            var asignacion = await identidad.UsuarioSucursales.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(a => a.UsuarioId == usuarioId && a.SucursalId == id, ct);
            if (asignacion is null)
                await mediator.Send(new AsignarUsuarioASucursalCommand(id, usuarioId), ct);
            else if (asignacion.Estatus != EstatusCatalogo.Activo)
                await mediator.Send(new ReactivarAsignacionUsuarioSucursalCommand(id, usuarioId), ct);
        }
    }
}
