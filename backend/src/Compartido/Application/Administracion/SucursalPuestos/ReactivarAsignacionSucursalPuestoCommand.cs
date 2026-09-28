using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// Reactiva una asignación Sucursal ↔ Puesto ↔ Departamento previamente
/// desactivada (F1-ADM-01 Fase 2, reabierta 2026-09-24). Análogo de
/// <c>ReactivarAsignacionSucursalDepartamentoCommand</c>. La fila se
/// identifica por la terna (sucursal, puesto, departamento). Idempotente:
/// si ya está Activa, no-op.
/// </summary>
public sealed record ReactivarAsignacionSucursalPuestoCommand(
    Guid SucursalId,
    Guid PuestoId,
    Guid DepartamentoId) : IRequest<SucursalPuestoResponse>;

public sealed class ReactivarAsignacionSucursalPuestoHandler
    : IRequestHandler<ReactivarAsignacionSucursalPuestoCommand, SucursalPuestoResponse>
{
    private readonly CompartidoDbContext _db;

    public ReactivarAsignacionSucursalPuestoHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalPuestoResponse> Handle(
        ReactivarAsignacionSucursalPuestoCommand command,
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

        if (asignacion.Estatus != EstatusCatalogo.Activo)
        {
            asignacion.Activar();
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
