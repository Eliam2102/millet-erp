using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// Desactiva una asignación Sucursal ↔ Puesto (F1-ADM-01 Fase 2). Análogo
/// exacto de <c>DesactivarAsignacionSucursalDepartamentoCommand</c>.
/// Idempotente: si ya está Inactiva, no-op.
/// </summary>
public sealed record DesactivarAsignacionSucursalPuestoCommand(
    Guid SucursalId,
    Guid PuestoId) : IRequest<SucursalPuestoResponse>;

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
                  && a.PuestoId == command.PuestoId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "SUCURSAL_PUESTO_NO_ENCONTRADA",
                $"No existe asignación para sucursal '{command.SucursalId}' y puesto '{command.PuestoId}'.");

        if (asignacion.Estatus != EstatusCatalogo.Inactivo)
        {
            asignacion.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        var puesto = await _db.Puestos.AsNoTracking()
            .FirstAsync(p => p.Id == asignacion.PuestoId, cancellationToken);

        return new SucursalPuestoResponse(
            asignacion.SucursalId,
            asignacion.PuestoId,
            puesto.Clave,
            puesto.Nombre,
            asignacion.Estatus,
            asignacion.Version);
    }
}
