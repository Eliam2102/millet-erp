using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Reactiva un usuario previamente desactivado (F-Admin-PR4.2).
/// Idempotente: si ya está activo, no-op. 404 si no existe.
/// </summary>
public sealed record ReactivarUsuarioCommand(Guid Id) : IRequest<UsuarioResponse>;

public sealed class ReactivarUsuarioHandler
    : IRequestHandler<ReactivarUsuarioCommand, UsuarioResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly IPermissionCache _permissionCache;

    public ReactivarUsuarioHandler(IdentidadDbContext db, CompartidoDbContext compartido,
        IPermissionCache permissionCache)
    {
        _db = db;
        _compartido = compartido;
        _permissionCache = permissionCache;
    }

    public async Task<UsuarioResponse> Handle(
        ReactivarUsuarioCommand command, CancellationToken cancellationToken)
    {
        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{command.Id}'.");

        if (!usuario.Activo)
        {
            var empleadoInactivo = await _compartido.Empleados.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(e => e.UsuarioId == usuario.Id && e.Estatus != EstatusCatalogo.Activo,
                    cancellationToken);
            if (empleadoInactivo)
                throw new BusinessRuleException("COLABORADOR_INACTIVO",
                    "No se puede reactivar el acceso de un empleado dado de baja.");
            usuario.Reactivar();
            await _db.SaveChangesAsync(cancellationToken);
            await _permissionCache.InvalidateAllForUserAsync(usuario.Id, cancellationToken);
        }

        return new UsuarioResponse(
            usuario.Id,
            usuario.Email,
            usuario.EntraOid,
            usuario.Nombre,
            usuario.DepartamentoId,
            usuario.Activo,
            usuario.Version);
    }
}
