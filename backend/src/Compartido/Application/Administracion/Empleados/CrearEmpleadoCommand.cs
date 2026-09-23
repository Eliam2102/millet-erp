using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.Empleados;

/// <summary>
/// Crea un empleado (ADM-PR1). UNIQUE(empresa_id, clave) → 409
/// <c>EMPLEADO_CLAVE_DUPLICADA</c>. Las referencias (empresa, puesto,
/// jefe directo, sucursal, departamento) se validan cross-entity antes
/// de tocar la FK física. <c>UsuarioId</c> no se valida aquí
/// (cross-módulo Identidad, D5).
/// </summary>
public sealed record CrearEmpleadoCommand(
    Guid Id,
    Guid EmpresaId,
    string Clave,
    string Nombre,
    string? Email = null,
    Guid? PuestoId = null,
    Guid? JefeDirectoId = null,
    Guid? SucursalId = null,
    Guid? DepartamentoId = null,
    Guid? UsuarioId = null,
    string? CodigoNomina = null,
    string? EmailContacto = null) : IRequest<EmpleadoResponse>;

public sealed class CrearEmpleadoValidator : AbstractValidator<CrearEmpleadoCommand>
{
    public CrearEmpleadoValidator()
    {
        RuleFor(c => c.EmpresaId).NotEmpty();
        RuleFor(c => c.Clave).NotEmpty().MaximumLength(20);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.Email!).NotEmpty().EmailAddress().MaximumLength(254)
            .When(c => c.Email is not null);
        RuleFor(c => c.CodigoNomina!).NotEmpty().MaximumLength(20)
            .When(c => c.CodigoNomina is not null);
    }
}

public sealed class CrearEmpleadoHandler
    : IRequestHandler<CrearEmpleadoCommand, EmpleadoResponse>
{
    private readonly CompartidoDbContext _db;

    public CrearEmpleadoHandler(CompartidoDbContext db) => _db = db;

    public async Task<EmpleadoResponse> Handle(
        CrearEmpleadoCommand command, CancellationToken cancellationToken)
    {
        var empresaExiste = await _db.Empresas.AsNoTracking()
            .AnyAsync(e => e.Id == command.EmpresaId, cancellationToken);
        if (!empresaExiste)
        {
            throw new BusinessRuleException(
                "EMPLEADO_EMPRESA_NO_EXISTE",
                $"No existe la empresa '{command.EmpresaId}'.");
        }

        var claveExiste = await _db.Empleados.AsNoTracking()
            .AnyAsync(e => e.EmpresaId == command.EmpresaId && e.Clave == command.Clave,
                cancellationToken);
        if (claveExiste)
        {
            throw new ConflictException(
                "EMPLEADO_CLAVE_DUPLICADA",
                $"Ya existe un empleado con clave '{command.Clave}' en la empresa.");
        }

        if (command.PuestoId is Guid puestoId)
            await ValidacionesEmpleado.ValidarPuestoAsync(_db, puestoId, command.EmpresaId, cancellationToken);
        if (command.JefeDirectoId is Guid jefeId)
            await ValidacionesEmpleado.ValidarJefeDirectoAsync(_db, jefeId, command.EmpresaId, cancellationToken);
        if (command.SucursalId is Guid sucursalId)
            await ValidacionesEmpleado.ValidarSucursalAsync(_db, sucursalId, command.EmpresaId, cancellationToken);
        if (command.DepartamentoId is Guid departamentoId)
            await ValidacionesEmpleado.ValidarDepartamentoAsync(_db, departamentoId, command.EmpresaId, cancellationToken);

        if (command.SucursalId is Guid sucursalValida)
        {
            if (command.DepartamentoId is Guid deptoValido)
                await ValidacionesEmpleado.ValidarDepartamentoDeSucursalAsync(_db, sucursalValida, deptoValido, cancellationToken);
            if (command.PuestoId is Guid puestoValido)
                await ValidacionesEmpleado.ValidarPuestoDeSucursalAsync(_db, sucursalValida, puestoValido, command.DepartamentoId, cancellationToken);
        }

        var id = command.Id == Guid.Empty ? Guid.CreateVersion7() : command.Id;
        var empleado = new Empleado(
            id,
            command.EmpresaId,
            command.Clave,
            command.Nombre,
            email: command.Email,
            puestoId: command.PuestoId,
            jefeDirectoId: command.JefeDirectoId,
            sucursalId: command.SucursalId,
            departamentoId: command.DepartamentoId,
            usuarioId: command.UsuarioId,
            codigoNomina: command.CodigoNomina,
            emailContacto: command.EmailContacto);

        _db.Empleados.Add(empleado);
        await _db.SaveChangesAsync(cancellationToken);

        return Mapear(empleado);
    }

    internal static EmpleadoResponse Mapear(Empleado e) => new(
        e.Id, e.EmpresaId, e.Clave, e.Nombre, e.Email,
        e.PuestoId, e.JefeDirectoId, e.SucursalId, e.DepartamentoId,
        e.UsuarioId, e.CodigoNomina, e.Estatus, e.Version);
}
