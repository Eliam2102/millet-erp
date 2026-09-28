using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Empleados;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>Edición del empleado con sincronización del departamento y cuenta Entra ID de su usuario.</summary>
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
    private readonly IEntraDirectorioPort _directorio;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _entraOptions;

    public GestionColaboradorHandlers(
        IMediator mediator,
        CompartidoDbContext compartido,
        IdentidadDbContext identidad,
        TransaccionColaborador transaccion,
        IEntraDirectorioPort directorio,
        IOptionsMonitor<EntraDirectorioOptions> entraOptions)
    {
        _mediator = mediator;
        _compartido = compartido;
        _identidad = identidad;
        _transaccion = transaccion;
        _directorio = directorio;
        _entraOptions = entraOptions;
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
                .Select(e => new { e.UsuarioId, e.Email, e.EmpresaId })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new EntityNotFoundException("EMPLEADO_NO_ENCONTRADO", "No existe el empleado.");

            if (patch.LimpiarEmail && actual.UsuarioId.HasValue)
            {
                throw new BusinessRuleException("COLABORADOR_EMAIL_REQUERIDO_PARA_USUARIO",
                    "No se puede eliminar el correo corporativo de un colaborador con usuario activo en el ERP.");
            }

            CuentaEntra? cuentaEntraNueva = null;
            var nuevoEmail = patch.Email?.Trim();
            bool emailCambio = nuevoEmail != null && !string.Equals(nuevoEmail, actual.Email, StringComparison.OrdinalIgnoreCase);

            if (emailCambio)
            {
                if (!_entraOptions.CurrentValue.PermiteCorreo(nuevoEmail!))
                {
                    throw new BusinessRuleException("DOMINIO_NO_PERMITIDO",
                        "El dominio del correo no está dentro de los dominios corporativos autorizados.");
                }

                if (actual.UsuarioId is Guid uId)
                {
                    var cuenta = await _directorio.BuscarPorCorreoAsync(nuevoEmail!, cancellationToken);
                    if (cuenta is null)
                    {
                        throw new BusinessRuleException("ENTRA_CUENTA_NO_ENCONTRADA",
                            $"No se encontró una cuenta en Microsoft Entra ID para el correo '{nuevoEmail}'. Debe existir previamente en el tenant.");
                    }
                    if (!cuenta.Habilitada)
                    {
                        throw new BusinessRuleException("ENTRA_CUENTA_DESHABILITADA",
                            $"La cuenta de Microsoft Entra ID para '{nuevoEmail}' se encuentra deshabilitada.");
                    }

                    var otroUsuario = await _identidad.Usuarios.IgnoreQueryFilters().AnyAsync(
                        u => u.Id != uId && (u.Email == nuevoEmail || u.EntraOid == cuenta.ObjectId),
                        cancellationToken);
                    if (otroUsuario)
                    {
                        throw new BusinessRuleException("USUARIO_YA_VINCULADO",
                            "Ya existe otro usuario registrado con este correo o cuenta de Microsoft Entra ID.");
                    }

                    cuentaEntraNueva = cuenta;
                }

                var otroEmpleado = await _compartido.Empleados.IgnoreQueryFilters().AnyAsync(
                    e => e.Id != patch.Id && e.Email == nuevoEmail,
                    cancellationToken);
                if (otroEmpleado)
                {
                    throw new BusinessRuleException("EMPLEADO_CORREO_DUPLICADO",
                        "Ya existe otro empleado registrado con este correo corporativo.");
                }
            }

            var actualizado = await _mediator.Send(patch, cancellationToken);

            if (actual.UsuarioId is Guid usuarioId)
            {
                var usuario = await _identidad.Usuarios.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(u => u.Id == usuarioId, cancellationToken)
                    ?? throw new BusinessRuleException("COLABORADOR_USUARIO_INCONSISTENTE",
                        "El empleado apunta a un usuario que ya no existe.");

                if (emailCambio && cuentaEntraNueva != null)
                {
                    usuario.ActualizarCuentaEntra(nuevoEmail!, cuentaEntraNueva.ObjectId);
                }

                if (patch.DepartamentoId.HasValue || patch.LimpiarDepartamento)
                {
                    usuario.AsignarDepartamento(patch.LimpiarDepartamento ? null : patch.DepartamentoId);
                }

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
