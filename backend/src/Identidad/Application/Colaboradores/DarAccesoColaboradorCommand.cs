using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Administracion.Application.Empleados;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Application.UsuarioSucursales;
using Millet.Identidad.Application.Usuarios;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Colaboradores;

/// <summary>Da acceso a un empleado creado inicialmente sin usuario.</summary>
public sealed record DarAccesoColaboradorCommand(
    Guid EmpleadoId,
    TipoAccesoColaborador Acceso,
    string CorreoCorporativo,
    Guid? RolId = null,
    string? EmailContacto = null) : IRequest<AltaColaboradorResponse>;

public sealed class DarAccesoColaboradorValidator : AbstractValidator<DarAccesoColaboradorCommand>
{
    public DarAccesoColaboradorValidator()
    {
        RuleFor(x => x.EmpleadoId).NotEmpty();
        RuleFor(x => x.Acceso).Must(x => x is TipoAccesoColaborador.CuentaExistente or TipoAccesoColaborador.CuentaNueva);
        RuleFor(x => x.CorreoCorporativo).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(x => x.EmailContacto!).EmailAddress().MaximumLength(254)
            .When(x => !string.IsNullOrWhiteSpace(x.EmailContacto));
    }
}

public sealed class DarAccesoColaboradorHandler : IRequestHandler<DarAccesoColaboradorCommand, AltaColaboradorResponse>
{
    private readonly IMediator _mediator;
    private readonly IdentidadDbContext _identidad;
    private readonly CompartidoDbContext _compartido;
    private readonly TransaccionColaborador _transaccion;
    private readonly IEntraDirectorioPort _directorio;
    private readonly IOptionsMonitor<EntraDirectorioOptions> _options;

    public DarAccesoColaboradorHandler(
        IMediator mediator,
        IdentidadDbContext identidad,
        CompartidoDbContext compartido,
        TransaccionColaborador transaccion,
        IEntraDirectorioPort directorio,
        IOptionsMonitor<EntraDirectorioOptions> options)
    {
        _mediator = mediator;
        _identidad = identidad;
        _compartido = compartido;
        _transaccion = transaccion;
        _directorio = directorio;
        _options = options;
    }

    public async Task<AltaColaboradorResponse> Handle(
        DarAccesoColaboradorCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var empleado = await _compartido.Empleados
            .SingleOrDefaultAsync(e => e.Id == request.EmpleadoId, ct)
            ?? throw new EntityNotFoundException("EMPLEADO_NO_ENCONTRADO", "No existe el empleado.");
        if (empleado.UsuarioId.HasValue)
            throw new ConflictException("COLABORADOR_YA_TIENE_ACCESO", "El empleado ya tiene un usuario vinculado.");
        if (empleado.Estatus != EstatusCatalogo.Activo)
            throw new BusinessRuleException("COLABORADOR_INACTIVO", "Reactiva al empleado antes de darle acceso.");
        if (empleado.SucursalId is not Guid sucursalId ||
            empleado.DepartamentoId is not Guid departamentoId ||
            empleado.PuestoId is not Guid puestoId)
            throw new BusinessRuleException("COLABORADOR_ESTRUCTURA_INCOMPLETA",
                "Asigna sucursal, departamento y puesto antes de dar acceso.");

        var correo = request.CorreoCorporativo.Trim();
        if (!_options.CurrentValue.PermiteCorreo(correo))
            throw new BusinessRuleException("ENTRA_DOMINIO_NO_PERMITIDO", "El dominio corporativo no está permitido.");
        var contacto = request.EmailContacto?.Trim() ?? empleado.EmailContacto;
        if (request.Acceso == TipoAccesoColaborador.CuentaNueva && string.IsNullOrWhiteSpace(contacto))
            throw new BusinessRuleException("COLABORADOR_SIN_CORREO_CONTACTO",
                "Se requiere un correo personal o de contacto para enviar la contraseña temporal.");

        var rolId = request.RolId ?? await _compartido.Puestos.AsNoTracking()
            .Where(p => p.Id == puestoId).Select(p => p.RolSugeridoId)
            .FirstOrDefaultAsync(ct)
            ?? throw new BusinessRuleException("ALTA_ROL_REQUERIDO", "Indica un rol para el acceso.");

        CuentaEntra? cuenta = null;
        Usuario? reutilizable = null;
        if (request.Acceso == TipoAccesoColaborador.CuentaExistente)
        {
            cuenta = await _directorio.BuscarPorCorreoAsync(correo, ct)
                ?? throw new BusinessRuleException("ENTRA_CUENTA_NO_ENCONTRADA", "La cuenta Microsoft no existe.");
            if (!cuenta.Habilitada)
                throw new BusinessRuleException("ENTRA_CUENTA_DESHABILITADA", "La cuenta Microsoft está deshabilitada.");
            var candidatos = await _identidad.Usuarios.IgnoreQueryFilters()
                .Where(u => u.Email == correo || u.EntraOid == cuenta.ObjectId).ToListAsync(ct);
            if (candidatos.Count > 1)
                throw new ConflictException("USUARIO_CORREO_OID_INCONSISTENTE",
                    "El correo y el OID pertenecen a usuarios distintos.");
            reutilizable = candidatos.SingleOrDefault();
            if (reutilizable is not null)
            {
                if (reutilizable.EsCuentaTecnica || !reutilizable.Activo ||
                    await _compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
                        .AnyAsync(e => e.UsuarioId == reutilizable.Id, ct))
                    throw new ConflictException("USUARIO_NO_REUTILIZABLE",
                        "La cuenta ya está vinculada, inactiva o es técnica.");
            }
        }
        else
        {
            if (await _directorio.BuscarPorCorreoAsync(correo, ct) is not null)
                throw new ConflictException("ENTRA_UPN_EN_USO", "La cuenta Microsoft ya existe.");
            if (await _identidad.Usuarios.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(u => u.Email == correo, ct))
                throw new ConflictException("USUARIO_EMAIL_DUPLICADO", "El correo ya existe en el ERP.");
        }

        return await _transaccion.EjecutarAsync(async () =>
        {
            Guid usuarioId;
            if (reutilizable is not null)
            {
                reutilizable.VincularEntraOid(cuenta!.ObjectId);
                reutilizable.AsignarDepartamento(departamentoId);
                await _identidad.SaveChangesAsync(ct);
                usuarioId = reutilizable.Id;
            }
            else
            {
                var creado = await _mediator.Send(new CrearUsuarioCommand(Guid.Empty, correo,
                    cuenta?.ObjectId, empleado.Nombre, departamentoId), ct);
                usuarioId = creado.Id;
                if (request.Acceso == TipoAccesoColaborador.CuentaNueva)
                {
                    var usuario = await _identidad.Usuarios.SingleAsync(u => u.Id == usuarioId, ct);
                    usuario.IniciarProvision();
                    await _identidad.SaveChangesAsync(ct);
                }
            }

            empleado.ActualizarDatos(email: correo, usuarioId: usuarioId,
                emailContacto: request.EmailContacto?.Trim());
            await _compartido.SaveChangesAsync(ct);
            await _mediator.Send(new AsignarRolAUsuarioCommand(usuarioId, empleado.EmpresaId, rolId), ct);
            await _mediator.Send(new AsignarUsuarioASucursalCommand(sucursalId, usuarioId), ct);

            var final = await _identidad.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuarioId, ct);
            var empleadoResponse = new EmpleadoResponse(
                empleado.Id, empleado.EmpresaId, empleado.Clave, empleado.Nombre, empleado.Email,
                empleado.PuestoId, empleado.JefeDirectoId, empleado.SucursalId, empleado.DepartamentoId,
                empleado.UsuarioId, empleado.CodigoNomina, empleado.Estatus, empleado.Version);
            return new AltaColaboradorResponse(empleadoResponse,
                new AccesoColaboradorResponse(usuarioId, correo, final.EntraOid,
                    final.EstadoAcceso, reutilizable is not null, empleado.EmpresaId, rolId, sucursalId));
        }, ct);
    }
}
