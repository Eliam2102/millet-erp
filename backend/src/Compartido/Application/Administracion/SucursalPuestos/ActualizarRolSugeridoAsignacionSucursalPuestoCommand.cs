using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// PATCH del rol sugerido de una asignación puntual Sucursal ↔ Puesto ↔
/// Departamento (F1-ADM-01.4 reabierta, pedido del owner 2026-09-24).
/// Reemplazo directo, no el patrón "null = no tocar" del resto del
/// módulo: <see cref="RolSugeridoId"/> con valor fija la excepción de
/// esta asignación; <c>null</c> la limpia (vuelve a heredar el rol
/// sugerido del puesto). Sigue siendo sólo sugerencia (01-04): no
/// asigna rol a ningún usuario.
/// </summary>
public sealed record ActualizarRolSugeridoAsignacionSucursalPuestoCommand(
    Guid SucursalId,
    Guid PuestoId,
    Guid DepartamentoId,
    Guid? RolSugeridoId) : IRequest<SucursalPuestoResponse>;

public sealed class ActualizarRolSugeridoAsignacionSucursalPuestoValidator
    : AbstractValidator<ActualizarRolSugeridoAsignacionSucursalPuestoCommand>
{
    public ActualizarRolSugeridoAsignacionSucursalPuestoValidator()
    {
        RuleFor(c => c.SucursalId).NotEmpty().WithErrorCode("SUCURSAL_REQUERIDA");
        RuleFor(c => c.PuestoId).NotEmpty().WithErrorCode("PUESTO_REQUERIDO");
        RuleFor(c => c.DepartamentoId).NotEmpty().WithErrorCode("DEPARTAMENTO_REQUERIDO");
    }
}

public sealed class ActualizarRolSugeridoAsignacionSucursalPuestoHandler
    : IRequestHandler<ActualizarRolSugeridoAsignacionSucursalPuestoCommand, SucursalPuestoResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly IRolReadPort _rolReadPort;

    public ActualizarRolSugeridoAsignacionSucursalPuestoHandler(
        CompartidoDbContext db, IRolReadPort rolReadPort)
    {
        _db = db;
        _rolReadPort = rolReadPort;
    }

    public async Task<SucursalPuestoResponse> Handle(
        ActualizarRolSugeridoAsignacionSucursalPuestoCommand command,
        CancellationToken cancellationToken)
    {
        var asignacion = await _db.SucursalPuestos
            .FirstOrDefaultAsync(
                a => a.SucursalId == command.SucursalId
                  && a.PuestoId == command.PuestoId
                  && a.DepartamentoId == command.DepartamentoId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_PUESTO_NO_ENCONTRADA",
                $"No existe asignación para sucursal '{command.SucursalId}', puesto '{command.PuestoId}' " +
                $"y departamento '{command.DepartamentoId}'.");

        if (command.RolSugeridoId is Guid rolId)
        {
            var rolActivo = await _rolReadPort.ExisteActivoAsync(rolId, cancellationToken);
            if (!rolActivo)
            {
                throw new ConflictException(
                    "ROL_SUGERIDO_INVALIDO",
                    $"El rol sugerido '{rolId}' no existe o está inactivo.");
            }

            asignacion.FijarRolSugerido(rolId);
        }
        else
        {
            asignacion.LimpiarRolSugerido();
        }

        await _db.SaveChangesAsync(cancellationToken);

        var puesto = await _db.Puestos.AsNoTracking()
            .FirstAsync(p => p.Id == asignacion.PuestoId, cancellationToken);

        var deptoNombre = await _db.Departamentos.AsNoTracking()
            .Where(d => d.Id == asignacion.DepartamentoId)
            .Select(d => d.Nombre)
            .FirstOrDefaultAsync(cancellationToken);

        return new SucursalPuestoResponse(
            asignacion.SucursalId,
            asignacion.PuestoId,
            puesto.Clave,
            puesto.Nombre,
            asignacion.DepartamentoId,
            deptoNombre,
            asignacion.Estatus,
            asignacion.Version,
            asignacion.RolSugeridoId,
            asignacion.RolSugeridoId ?? puesto.RolSugeridoId);
    }
}
