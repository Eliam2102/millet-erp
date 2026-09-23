using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.UsuarioSucursales;

/// <summary>
/// Desactiva ("desasigna") una asignación Usuario ↔ Sucursal (F1-ADM-01
/// Fase 2). El plan de negocio usa el término "Desasignar"; se
/// implementa como Desactivar/Reactivar (mismo patrón que
/// <c>SucursalDepartamento</c>/<c>SucursalPuesto</c>) para permitir
/// reasignar sin recrear la fila. Idempotente: si ya está Inactiva,
/// no-op.
/// </summary>
public sealed record DesactivarAsignacionUsuarioSucursalCommand(
    Guid SucursalId,
    Guid UsuarioId) : IRequest<UsuarioSucursalResponse>;

public sealed class DesactivarAsignacionUsuarioSucursalHandler
    : IRequestHandler<DesactivarAsignacionUsuarioSucursalCommand, UsuarioSucursalResponse>
{
    private readonly IdentidadDbContext _db;

    public DesactivarAsignacionUsuarioSucursalHandler(IdentidadDbContext db) => _db = db;

    public async Task<UsuarioSucursalResponse> Handle(
        DesactivarAsignacionUsuarioSucursalCommand command,
        CancellationToken cancellationToken)
    {
        var asignacion = await _db.UsuarioSucursales
            .FirstOrDefaultAsync(
                a => a.UsuarioId == command.UsuarioId && a.SucursalId == command.SucursalId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_SUCURSAL_NO_ENCONTRADA",
                $"No existe asignación para usuario '{command.UsuarioId}' y sucursal '{command.SucursalId}'.");

        if (asignacion.Estatus != EstatusCatalogo.Inactivo)
        {
            asignacion.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        var usuario = await _db.Usuarios.AsNoTracking()
            .FirstAsync(u => u.Id == asignacion.UsuarioId, cancellationToken);

        return new UsuarioSucursalResponse(
            asignacion.SucursalId,
            asignacion.UsuarioId,
            usuario.Email,
            usuario.Nombre,
            asignacion.Estatus,
            asignacion.Version);
    }
}
