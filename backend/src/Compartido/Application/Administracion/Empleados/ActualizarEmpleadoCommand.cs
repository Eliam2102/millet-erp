using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empleados;

/// <summary>
/// PATCH parcial sobre Empleado (ADM-PR1). Convención: <c>null</c> = no
/// tocar; los flags <c>Limpiar*</c> ponen el campo nullable en null.
/// Inmutables: Clave (business key) y EmpresaId.
/// </summary>
public sealed record ActualizarEmpleadoCommand(
    Guid Id,
    string? Nombre = null,
    string? Email = null,
    bool LimpiarEmail = false,
    Guid? PuestoId = null,
    bool LimpiarPuesto = false,
    Guid? JefeDirectoId = null,
    bool LimpiarJefeDirecto = false,
    Guid? SucursalId = null,
    bool LimpiarSucursal = false,
    Guid? DepartamentoId = null,
    bool LimpiarDepartamento = false,
    Guid? UsuarioId = null,
    bool LimpiarUsuario = false,
    string? CodigoNomina = null,
    bool LimpiarCodigoNomina = false,
    string? EmailContacto = null,
    bool LimpiarEmailContacto = false) : IRequest<EmpleadoResponse>;

public sealed class ActualizarEmpleadoValidator : AbstractValidator<ActualizarEmpleadoCommand>
{
    public ActualizarEmpleadoValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Nombre!).NotEmpty().MaximumLength(254)
            .When(c => c.Nombre is not null);
        RuleFor(c => c.Email!).NotEmpty().EmailAddress().MaximumLength(254)
            .When(c => c.Email is not null);
        RuleFor(c => c.CodigoNomina!).NotEmpty().MaximumLength(20)
            .When(c => c.CodigoNomina is not null);
        RuleFor(c => c.EmailContacto!).NotEmpty().EmailAddress().MaximumLength(254)
            .When(c => c.EmailContacto is not null);
        RuleFor(c => c.EmailContacto).Null()
            .When(c => c.LimpiarEmailContacto)
            .WithMessage("No se puede enviar EmailContacto y LimpiarEmailContacto a la vez.");
        RuleFor(c => c.Email).Null()
            .When(c => c.LimpiarEmail)
            .WithMessage("No se puede enviar Email y LimpiarEmail a la vez.");
        RuleFor(c => c.PuestoId).Null()
            .When(c => c.LimpiarPuesto)
            .WithMessage("No se puede enviar PuestoId y LimpiarPuesto a la vez.");
        RuleFor(c => c.JefeDirectoId).Null()
            .When(c => c.LimpiarJefeDirecto)
            .WithMessage("No se puede enviar JefeDirectoId y LimpiarJefeDirecto a la vez.");
        RuleFor(c => c.SucursalId).Null()
            .When(c => c.LimpiarSucursal)
            .WithMessage("No se puede enviar SucursalId y LimpiarSucursal a la vez.");
        RuleFor(c => c.DepartamentoId).Null()
            .When(c => c.LimpiarDepartamento)
            .WithMessage("No se puede enviar DepartamentoId y LimpiarDepartamento a la vez.");
        RuleFor(c => c.UsuarioId).Null()
            .When(c => c.LimpiarUsuario)
            .WithMessage("No se puede enviar UsuarioId y LimpiarUsuario a la vez.");
        RuleFor(c => c.CodigoNomina).Null()
            .When(c => c.LimpiarCodigoNomina)
            .WithMessage("No se puede enviar CodigoNomina y LimpiarCodigoNomina a la vez.");
    }
}

public sealed class ActualizarEmpleadoHandler
    : IRequestHandler<ActualizarEmpleadoCommand, EmpleadoResponse>
{
    private readonly CompartidoDbContext _db;

    public ActualizarEmpleadoHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpleadoResponse> Handle(
        ActualizarEmpleadoCommand command, CancellationToken cancellationToken)
    {
        var empleado = await _db.Empleados
            .FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "EMPLEADO_NO_ENCONTRADO",
                $"No existe empleado con id '{command.Id}'.");

        if (command.PuestoId is Guid puestoId)
            await ValidacionesEmpleado.ValidarPuestoAsync(_db, puestoId, empleado.EmpresaId, cancellationToken);
        if (command.JefeDirectoId is Guid jefeId)
            await ValidacionesEmpleado.ValidarJefeDirectoAsync(_db, jefeId, empleado.EmpresaId, cancellationToken);
        if (command.SucursalId is Guid sucursalId)
            await ValidacionesEmpleado.ValidarSucursalAsync(_db, sucursalId, empleado.EmpresaId, cancellationToken);
        if (command.DepartamentoId is Guid departamentoId)
            await ValidacionesEmpleado.ValidarDepartamentoAsync(_db, departamentoId, empleado.EmpresaId, cancellationToken);

        var sucursalEfectiva = command.LimpiarSucursal ? null : (command.SucursalId ?? empleado.SucursalId);
        var deptoEfectivo = command.LimpiarDepartamento ? null : (command.DepartamentoId ?? empleado.DepartamentoId);
        var puestoEfectivo = command.LimpiarPuesto ? null : (command.PuestoId ?? empleado.PuestoId);

        if (sucursalEfectiva is Guid sucursalValida)
        {
            if (deptoEfectivo is Guid deptoValido)
                await ValidacionesEmpleado.ValidarDepartamentoDeSucursalAsync(_db, sucursalValida, deptoValido, cancellationToken);
            if (puestoEfectivo is Guid puestoValido)
                await ValidacionesEmpleado.ValidarPuestoDeSucursalAsync(_db, sucursalValida, puestoValido, deptoEfectivo, cancellationToken);
        }

        empleado.ActualizarDatos(
            nombre: command.Nombre,
            email: command.Email,
            limpiarEmail: command.LimpiarEmail,
            puestoId: command.PuestoId,
            limpiarPuesto: command.LimpiarPuesto,
            jefeDirectoId: command.JefeDirectoId,
            limpiarJefeDirecto: command.LimpiarJefeDirecto,
            sucursalId: command.SucursalId,
            limpiarSucursal: command.LimpiarSucursal,
            departamentoId: command.DepartamentoId,
            limpiarDepartamento: command.LimpiarDepartamento,
            usuarioId: command.UsuarioId,
            limpiarUsuario: command.LimpiarUsuario,
            codigoNomina: command.CodigoNomina,
            limpiarCodigoNomina: command.LimpiarCodigoNomina,
            emailContacto: command.EmailContacto,
            limpiarEmailContacto: command.LimpiarEmailContacto);
        await _db.SaveChangesAsync(cancellationToken);

        return CrearEmpleadoHandler.Mapear(empleado);
    }
}
