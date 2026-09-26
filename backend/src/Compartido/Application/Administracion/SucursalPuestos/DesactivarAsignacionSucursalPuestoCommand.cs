using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// Desactiva una asignación Sucursal ↔ Puesto ↔ Departamento (F1-ADM-01
/// Fase 2, reabierta 2026-09-24). Análogo de
/// <c>DesactivarAsignacionSucursalDepartamentoCommand</c>. La fila se
/// identifica por la terna (sucursal, puesto, departamento) — desactivar
/// un departamento del puesto NO afecta sus otras asignaciones activas
/// en la misma sucursal. Idempotente: si ya está Inactiva, no-op.
/// </summary>
public sealed record DesactivarAsignacionSucursalPuestoCommand(
    Guid SucursalId,
    Guid PuestoId,
    Guid DepartamentoId) : IRequest<SucursalPuestoResponse>;

public sealed class DesactivarAsignacionSucursalPuestoHandler
    : IRequestHandler<DesactivarAsignacionSucursalPuestoCommand, SucursalPuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarAsignacionSucursalPuestoHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalPuestoResponse> Handle(
        DesactivarAsignacionSucursalPuestoCommand command,
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

        if (asignacion.Estatus != EstatusCatalogo.Inactivo)
        {
            asignacion.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

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
