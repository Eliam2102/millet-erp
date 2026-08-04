using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalDepartamentos;

/// <summary>
/// Reactiva una asignación previamente desactivada (PR-A1). Idempotente:
/// si ya está Activa, no-op.
/// </summary>
public sealed record ReactivarAsignacionSucursalDepartamentoCommand(
    Guid SucursalId,
    Guid DepartamentoId) : IRequest<SucursalDepartamentoResponse>;

public sealed class ReactivarAsignacionSucursalDepartamentoHandler
    : IRequestHandler<ReactivarAsignacionSucursalDepartamentoCommand, SucursalDepartamentoResponse>
{
    private readonly CompartidoDbContext _db;

    public ReactivarAsignacionSucursalDepartamentoHandler(CompartidoDbContext db) => _db = db;

    public async Task<SucursalDepartamentoResponse> Handle(
        ReactivarAsignacionSucursalDepartamentoCommand command,
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

        if (asignacion.Estatus != EstatusCatalogo.Activo)
        {
            asignacion.Activar();
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
