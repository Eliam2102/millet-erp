using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalDepartamentos;

/// <summary>
/// Desactiva una asignación Sucursal ↔ Departamento (PR-A1). Bloquea
/// nuevas RQs con la combinación (consumido por PR-A2) pero NO afecta
/// RQs/OCs existentes. Idempotente: si ya está Inactiva, no-op.
/// </summary>
public sealed record DesactivarAsignacionSucursalDepartamentoCommand(
    Guid SucursalId,
    Guid DepartamentoId) : IRequest<SucursalDepartamentoResponse>;

public sealed class DesactivarAsignacionSucursalDepartamentoHandler
    : IRequestHandler<DesactivarAsignacionSucursalDepartamentoCommand, SucursalDepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public DesactivarAsignacionSucursalDepartamentoHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalDepartamentoResponse> Handle(
        DesactivarAsignacionSucursalDepartamentoCommand command,
        CancellationToken cancellationToken)
    {
        var asignacion = await _db.SucursalDepartamentos
            .FirstOrDefaultAsync(
                a => a.SucursalId == command.SucursalId
                  && a.DepartamentoId == command.DepartamentoId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_DEPARTAMENTO_NO_ENCONTRADA",
                $"No existe asignación para sucursal '{command.SucursalId}' y departamento '{command.DepartamentoId}'.");

        if (asignacion.Estatus != EstatusCatalogo.Inactivo)
        {
            asignacion.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        var depto = await _db.Departamentos.AsNoTracking()
            .FirstAsync(d => d.Id == asignacion.DepartamentoId, cancellationToken);

        return new SucursalDepartamentoResponse(
            asignacion.SucursalId,
            asignacion.DepartamentoId,
            depto.Clave,
            depto.Nombre,
            asignacion.Estatus,
            asignacion.Version);
    }
}
