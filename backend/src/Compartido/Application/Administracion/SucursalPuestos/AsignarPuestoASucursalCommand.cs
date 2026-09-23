using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// Asigna un puesto a una sucursal (F1-ADM-01 Fase 2). Análogo exacto de
/// <c>AsignarDepartamentoASucursalCommand</c>. La asignación nace Activa.
/// Si ya existe una asignación para el par (<see cref="SucursalId"/>,
/// <see cref="PuestoId"/>) — sea Activa o Inactiva — devuelve 409
/// <c>SUCURSAL_PUESTO_DUPLICADA</c>. Para reactivar una asignación
/// inactiva se usa el endpoint Reactivar.
/// </summary>
public sealed record AsignarPuestoASucursalCommand(
    Guid SucursalId,
    Guid PuestoId,
    Guid DepartamentoId) : IRequest<SucursalPuestoResponse>;

public sealed class AsignarPuestoASucursalValidator
    : AbstractValidator<AsignarPuestoASucursalCommand>
{
    public AsignarPuestoASucursalValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.PuestoId).NotEmpty().WithErrorCode("PUESTO_REQUERIDO");
        RuleFor(c => c.DepartamentoId).NotEmpty().WithErrorCode("DEPARTAMENTO_REQUERIDO");
    }
}

public sealed class AsignarPuestoASucursalHandler
    : IRequestHandler<AsignarPuestoASucursalCommand, SucursalPuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public AsignarPuestoASucursalHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalPuestoResponse> Handle(
        AsignarPuestoASucursalCommand command, CancellationToken cancellationToken)
    {
        var sucursal = await _db.Sucursales.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == command.SucursalId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{command.SucursalId}'.");

        var puesto = await _db.Puestos.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == command.PuestoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "PUESTO_NO_ENCONTRADO",
                $"No existe puesto con id '{command.PuestoId}'.");

        var depto = await _db.Departamentos.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == command.DepartamentoId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "DEPARTAMENTO_NO_ENCONTRADO",
                $"No existe departamento con id '{command.DepartamentoId}'.");

        if (sucursal.EmpresaId != puesto.EmpresaId)
        {
            throw new ConflictException(
                "RELACION_INVALIDA",
                "La sucursal y el puesto deben pertenecer a la misma empresa.");
        }

        if (sucursal.EmpresaId != depto.EmpresaId)
        {
            throw new ConflictException(
                "DEPARTAMENTO_OTRA_EMPRESA",
                "El departamento debe pertenecer a la misma empresa.");
        }

        if (depto.Estatus != EstatusCatalogo.Activo)
        {
            throw new ConflictException(
                "DEPARTAMENTO_INACTIVO",
                "El departamento no está activo.");
        }

        var existe = await _db.SucursalPuestos.AsNoTracking()
            .AnyAsync(
                a => a.SucursalId == command.SucursalId
                  && a.PuestoId == command.PuestoId,
                cancellationToken);
        if (existe)
        {
            throw new ConflictException(
                "SUCURSAL_PUESTO_DUPLICADA",
                $"El puesto '{puesto.Clave}' ya está asignado a la sucursal '{sucursal.Clave}'.");
        }

        var asignacion = new SucursalPuesto(
            Guid.CreateVersion7(),
            sucursal.EmpresaId,
            command.SucursalId,
            command.PuestoId,
            command.DepartamentoId);

        _db.SucursalPuestos.Add(asignacion);
        await _db.SaveChangesAsync(cancellationToken);

        return new SucursalPuestoResponse(
            asignacion.SucursalId,
            asignacion.PuestoId,
            puesto.Clave,
            puesto.Nombre,
            asignacion.DepartamentoId,
            depto.Nombre,
            asignacion.Estatus,
            asignacion.Version);
    }
}
