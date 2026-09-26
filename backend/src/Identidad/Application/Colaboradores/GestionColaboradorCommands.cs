using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Empleados;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>Edición del empleado con sincronización del departamento de su usuario.</summary>
public sealed record ActualizarColaboradorCommand(ActualizarEmpleadoCommand Empleado) : IRequest<EmpleadoResponse>;

/// <summary>Baja conjunta: el usuario ya no puede iniciar sesión en ERP.</summary>
public sealed record DesactivarColaboradorCommand(Guid EmpleadoId) : IRequest<EmpleadoResponse>;

/// <summary>Recontratación laboral; el acceso al ERP se reactiva explícitamente.</summary>
public sealed record ReactivarColaboradorCommand(Guid EmpleadoId) : IRequest<EmpleadoResponse>;

public sealed class GestionColaboradorHandlers :
    IRequestHandler<ActualizarColaboradorCommand, EmpleadoResponse>,
    IRequestHandler<DesactivarColaboradorCommand, EmpleadoResponse>,
    IRequestHandler<ReactivarColaboradorCommand, EmpleadoResponse>
{
    private readonly IMediator _mediator;
    private readonly CompartidoDbContext _compartido;
    private readonly IdentidadDbContext _identidad;
    private readonly TransaccionColaborador _transaccion;

    public GestionColaboradorHandlers(
        IMediator mediator,
        CompartidoDbContext compartido,
        IdentidadDbContext identidad,
        TransaccionColaborador transaccion)
    {
        _mediator = mediator;
        _compartido = compartido;
        _identidad = identidad;
        _transaccion = transaccion;
    }

    public async Task<EmpleadoResponse> Handle(ActualizarColaboradorCommand request, CancellationToken cancellationToken)
    {
        var patch = request.Empleado;
        if (patch.UsuarioId is not null || patch.LimpiarUsuario)
            throw new BusinessRuleException("COLABORADOR_VINCULO_PROTEGIDO",
                "El vínculo de acceso se administra desde la sección Acceso, no desde el PATCH de empleado.");

        return await _transaccion.EjecutarAsync(async () =>
        {
            var actual = await _compartido.Empleados.AsNoTracking()
                .Where(e => e.Id == patch.Id)
                .Select(e => new { e.UsuarioId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException("EMPLEADO_NO_ENCONTRADO", "No existe el empleado.");

            var actualizado = await _mediator.Send(patch, cancellationToken);
            if (actual.UsuarioId is Guid usuarioId &&
                (patch.DepartamentoId.HasValue || patch.LimpiarDepartamento))
            {
                var usuario = await _identidad.Usuarios.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken)
                    ?? throw new BusinessRuleException("COLABORADOR_USUARIO_INCONSISTENTE",
                        "El empleado apunta a un usuario que ya no existe.");
                usuario.AsignarDepartamento(patch.LimpiarDepartamento ? null : patch.DepartamentoId);
                await _identidad.SaveChangesAsync(cancellationToken);
            }
            return actualizado;
        }, cancellationToken);
    }

    public Task<EmpleadoResponse> Handle(DesactivarColaboradorCommand request, CancellationToken cancellationToken) =>
        _transaccion.EjecutarAsync(async () =>
        {
            var usuarioId = await ObtenerUsuarioIdAsync(request.EmpleadoId, cancellationToken);
            // La protección de último super-admin se evalúa antes del commit.
            if (usuarioId is Guid id)
                await _mediator.Send(new Millet.Identidad.Application.Usuarios.DesactivarUsuarioCommand(id), cancellationToken);
            return await _mediator.Send(new DesactivarEmpleadoCommand(request.EmpleadoId), cancellationToken);
        }, cancellationToken);

    public Task<EmpleadoResponse> Handle(ReactivarColaboradorCommand request, CancellationToken cancellationToken) =>
        _transaccion.EjecutarAsync(async () =>
        {
            var reactivado = await _mediator.Send(new ReactivarEmpleadoCommand(request.EmpleadoId), cancellationToken);
            var usuarioId = await ObtenerUsuarioIdAsync(request.EmpleadoId, cancellationToken);
            if (usuarioId is Guid id)
                await _mediator.Send(new Millet.Identidad.Application.Usuarios.ReactivarUsuarioCommand(id), cancellationToken);
            return reactivado;
        }, cancellationToken);

    private async Task<Guid?> ObtenerUsuarioIdAsync(Guid empleadoId, CancellationToken ct) =>
        (await _compartido.Empleados.AsNoTracking()
            .Where(e => e.Id == empleadoId)
            .Select(e => new { e.UsuarioId })
            .FirstOrDefaultAsync(ct))?.UsuarioId;
}
