using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Administracion.Application.Empleados;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Catalogos.Domain;

namespace Millet.Identidad.Application.UsuarioSucursales;

public sealed record AlcanceUsuarioResponse(bool EsCorporativo, IReadOnlyList<Guid> Sucursales);
public sealed record ObtenerAlcanceUsuarioQuery(Guid UsuarioId) : IRequest<AlcanceUsuarioResponse>;
public sealed record ActualizarAlcanceUsuarioCommand(Guid UsuarioId, bool EsCorporativo, Guid[] Sucursales) : IRequest<AlcanceUsuarioResponse>;

public sealed class AlcanceUsuarioHandlers(IdentidadDbContext db, CompartidoDbContext compartido,
    ICurrentEmpresaContext empresa, ICurrentUserPermissions permisos, IPermissionCache cache,
    TransaccionColaborador transaccion, IMediator mediator)
    : IRequestHandler<ObtenerAlcanceUsuarioQuery, AlcanceUsuarioResponse>,
      IRequestHandler<ActualizarAlcanceUsuarioCommand, AlcanceUsuarioResponse>
{
    private async Task<Guid> ValidarAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new ForbiddenException("EMPRESA_NO_SELECCIONADA", "Selecciona la empresa.");
        if (!await db.UsuarioEmpresaRoles.AnyAsync(a => a.UsuarioId == usuarioId && a.EmpresaId == empresaId, cancellationToken))
            throw new EntityNotFoundException("USUARIO_NO_ENCONTRADO", "El usuario no tiene acceso a esta empresa.");
        return empresaId;
    }
    public async Task<AlcanceUsuarioResponse> Handle(ObtenerAlcanceUsuarioQuery query, CancellationToken cancellationToken)
    {
        var empresaId = await ValidarAsync(query.UsuarioId, cancellationToken);
        var corporativo = await db.UsuarioEmpresaAlcances.AnyAsync(a => a.UsuarioId == query.UsuarioId && a.EmpresaId == empresaId && a.EsCorporativo, cancellationToken);
        var sucursales = await db.UsuarioSucursales.AsNoTracking()
            .Where(a => a.UsuarioId == query.UsuarioId && a.EmpresaId == empresaId && a.Estatus == EstatusCatalogo.Activo)
            .Select(a => a.SucursalId).ToListAsync(cancellationToken);
        return new(corporativo, corporativo ? Array.Empty<Guid>() : sucursales);
    }
    public async Task<AlcanceUsuarioResponse> Handle(ActualizarAlcanceUsuarioCommand command, CancellationToken cancellationToken)
    {
        var empresaId = await ValidarAsync(command.UsuarioId, cancellationToken);
        if (!await permisos.TieneAsync(PermisosCanonicos.AdminEmpleadosGestionarTodasSucursales, cancellationToken))
            throw new ForbiddenException("ALCANCE_EMPRESA_DENEGADO", "Se requiere administrar el alcance de toda la empresa.");
        var seleccionadas = (command.Sucursales ?? []).Distinct().ToArray();
        if (command.EsCorporativo && seleccionadas.Length > 0)
            throw new BusinessRuleException("CORPORATIVO_SIN_SUCURSAL", "Un usuario corporativo no se asigna a sucursales individuales.");
        if (await compartido.Sucursales.CountAsync(s => seleccionadas.Contains(s.Id) && s.EmpresaId == empresaId && s.Estatus == EstatusCatalogo.Activo, cancellationToken) != seleccionadas.Length)
            throw new BusinessRuleException("SUCURSAL_INVALIDA", "Selecciona sucursales activas de esta empresa.");
        var response = await transaccion.EjecutarAsync(async () =>
        {
            var alcance = await db.UsuarioEmpresaAlcances.SingleOrDefaultAsync(a => a.UsuarioId == command.UsuarioId && a.EmpresaId == empresaId, cancellationToken);
            if (alcance is null) db.UsuarioEmpresaAlcances.Add(new(Guid.CreateVersion7(), command.UsuarioId, empresaId, command.EsCorporativo));
            else alcance.Cambiar(command.EsCorporativo);
            var actuales = await db.UsuarioSucursales.Where(a => a.UsuarioId == command.UsuarioId && a.EmpresaId == empresaId).ToListAsync(cancellationToken);
            foreach (var actual in actuales)
                if (seleccionadas.Contains(actual.SucursalId)) actual.Activar(); else actual.Desactivar();
            foreach (var id in seleccionadas.Where(id => actuales.All(a => a.SucursalId != id)))
                db.UsuarioSucursales.Add(new(Guid.CreateVersion7(), command.UsuarioId, id, empresaId));
            // Corporativo pertenece a la empresa y no conserva una adscripción laboral a una sucursal.
            if (command.EsCorporativo)
            {
                var empleado = await compartido.Empleados.SingleOrDefaultAsync(e => e.UsuarioId == command.UsuarioId && e.EmpresaId == empresaId, cancellationToken);
                if (empleado is not null)
                    await mediator.Send(new ActualizarEmpleadoCommand(empleado.Id, LimpiarSucursal: true), cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
            return new AlcanceUsuarioResponse(command.EsCorporativo, seleccionadas);
        }, cancellationToken);
        await cache.InvalidateAsync(command.UsuarioId, empresaId, cancellationToken);
        return response;
    }
}
