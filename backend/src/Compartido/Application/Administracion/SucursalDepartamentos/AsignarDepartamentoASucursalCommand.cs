using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalDepartamentos;

/// <summary>
/// Asigna un departamento a una sucursal (PR-A1). La asignación nace
/// Activa. Si ya existe una asignación para el par
/// (<see cref="SucursalId"/>, <see cref="DepartamentoId"/>) — sea Activa
/// o Inactiva — devuelve 409 <c>SUCURSAL_DEPARTAMENTO_DUPLICADA</c>.
/// Para reactivar una asignación inactiva se usa el endpoint Reactivar.
/// </summary>
public sealed record AsignarDepartamentoASucursalCommand(
    Guid SucursalId,
    Guid DepartamentoId) : IRequest<SucursalDepartamentoResponse>;

public sealed class AsignarDepartamentoASucursalValidator
    : AbstractValidator<AsignarDepartamentoASucursalCommand>
{
    public AsignarDepartamentoASucursalValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.DepartamentoId).NotEmpty().WithErrorCode("DEPARTAMENTO_REQUERIDO");
    }
}

public sealed class AsignarDepartamentoASucursalHandler
    : IRequestHandler<AsignarDepartamentoASucursalCommand, SucursalDepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public AsignarDepartamentoASucursalHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalDepartamentoResponse> Handle(
        AsignarDepartamentoASucursalCommand command, CancellationToken cancellationToken)
    {
        var sucursal = await _db.Sucursales.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SucursalId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{command.SucursalId}'.");

        var depto = await _db.Departamentos.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == command.DepartamentoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPARTAMENTO_NO_ENCONTRADO",
                $"No existe departamento con id '{command.DepartamentoId}'.");

        if (sucursal.EmpresaId != depto.EmpresaId)
        {
            throw new ConflictException(
                "RELACION_INVALIDA",
                "La sucursal y el departamento deben pertenecer a la misma empresa.");
        }

        var existe = await _db.SucursalDepartamentos.AsNoTracking()
            .AnyAsync(
                a => a.SucursalId == command.SucursalId
                  && a.DepartamentoId == command.DepartamentoId,
                cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "SUCURSAL_DEPARTAMENTO_DUPLICADA",
                $"El departamento '{depto.Clave}' ya está asignado a la sucursal '{sucursal.Clave}'.");
        }

        var asignacion = new SucursalDepartamento(
            Guid.CreateVersion7(),
            sucursal.EmpresaId,
            command.SucursalId,
            command.DepartamentoId);

        _db.SucursalDepartamentos.Add(asignacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new SucursalDepartamentoResponse(
            asignacion.SucursalId,
            asignacion.DepartamentoId,
            depto.Clave,
            depto.Nombre,
            asignacion.Estatus,
            asignacion.Version);
    }
}
