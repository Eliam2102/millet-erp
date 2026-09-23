using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.UsuarioSucursales;

/// <summary>
/// Reactiva una asignación Usuario ↔ Sucursal previamente desactivada
/// (F1-ADM-01 Fase 2). Idempotente: si ya está Activa, no-op.
/// </summary>
public sealed record ReactivarAsignacionUsuarioSucursalCommand(
    Guid SucursalId,
    Guid UsuarioId) : IRequest<UsuarioSucursalResponse>;

public sealed class ReactivarAsignacionUsuarioSucursalHandler
    : IRequestHandler<ReactivarAsignacionUsuarioSucursalCommand, UsuarioSucursalResponse>
{
    private readonly IdentidadDbContext _db;

    public ReactivarAsignacionUsuarioSucursalHandler(IdentidadDbContext db) => _db = db;

    public async Task<UsuarioSucursalResponse> Handle(
        ReactivarAsignacionUsuarioSucursalCommand command,
        CancellationToken cancellationToken)
    {
        var asignacion = await _db.UsuarioSucursales
            .FirstOrDefaultAsync(
                a => a.UsuarioId == command.UsuarioId && a.SucursalId == command.SucursalId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_SUCURSAL_NO_ENCONTRADA",
                $"No existe asignación para usuario '{command.UsuarioId}' y sucursal '{command.SucursalId}'.");

        if (asignacion.Estatus != EstatusCatalogo.Activo)
        {
            asignacion.Activar();
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
