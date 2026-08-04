using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Desactiva un usuario (soft-delete: <c>Activo=false</c>)
/// (F-Admin-PR4.2). Idempotente: si ya está inactivo, no-op.
///
/// <para>
/// Invariante cross-entity: si el usuario es <b>el único super-admin
/// activo del sistema</b> (asignado al rol con código
/// <c>super-admin</c>), la operación falla con 422
/// <c>USUARIO_ULTIMO_SUPER_ADMIN</c>. Evita que un admin se "encierre
/// fuera" sin posibilidad de recuperar el acceso. Reusa el código
/// canónico <c>super-admin</c> del bootstrap (F-Admin-PR3.x).
/// </para>
/// </summary>
public sealed record DesactivarUsuarioCommand(Guid Id) : IRequest<UsuarioResponse>;

public sealed class DesactivarUsuarioHandler
    : IRequestHandler<DesactivarUsuarioCommand, UsuarioResponse>
{
    /// <summary>Código del rol super-admin (alineado con bootstrap).</summary>
    public const string SuperAdminCodigo = "super-admin";

    private readonly IdentidadDbContext _db;

    public DesactivarUsuarioHandler(IdentidadDbContext db) => _db = db;

    public async Task<UsuarioResponse> Handle(
        DesactivarUsuarioCommand command, CancellationToken cancellationToken)
    {
        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{command.Id}'.");

        if (usuario.Activo)
        {
            await EnsureNoEsUltimoSuperAdminAsync(usuario.Id, cancellationToken);

            usuario.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
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

    /// <summary>
    /// Cuenta cuántos usuarios <b>activos</b> tienen al menos una
    /// asignación al rol super-admin (cualquier empresa). Si el usuario
    /// a desactivar es el único, throw 422.
    /// </summary>
    private async Task EnsureNoEsUltimoSuperAdminAsync(
        Guid usuarioId, CancellationToken ct)
    {
        var superAdminRol = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Codigo == SuperAdminCodigo, ct);
        if (superAdminRol is null) return; // sistema sin super-admin (test edge); no aplica.

        // ¿El usuario a desactivar es super-admin?
        var esSuperAdmin = await _db.UsuarioEmpresaRoles.AsNoTracking()
            .AnyAsync(uer => uer.UsuarioId == usuarioId && uer.RolId == superAdminRol.Id, ct);
        if (!esSuperAdmin) return;

        // ¿Hay otro super-admin activo?
        var otroSuperAdminActivo = await (
            from uer in _db.UsuarioEmpresaRoles.AsNoTracking()
            join u in _db.Usuarios.AsNoTracking() on uer.UsuarioId equals u.Id
            where uer.RolId == superAdminRol.Id
                  && u.Activo
                  && u.Id != usuarioId
            select u.Id)
            .AnyAsync(ct);

        if (!otroSuperAdminActivo)
        {
            throw new BusinessRuleException(
                "USUARIO_ULTIMO_SUPER_ADMIN",
                "No se puede desactivar al único super-admin activo del sistema. " +
                "Asigne otro super-admin antes de desactivar éste.");
        }
    }
}
