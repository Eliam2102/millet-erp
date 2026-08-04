using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application.Events;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Revoca una asignación <c>UsuarioEmpresaRol</c> por id (F-Admin-PR4.2).
///
/// <list type="bullet">
///   <item>404 <c>USUARIO_ASIGNACION_NO_ENCONTRADA</c> si no existe.</item>
///   <item>422 <c>USUARIO_ULTIMO_SUPER_ADMIN</c> si la asignación a borrar
///         es la única asignación de super-admin a un usuario activo en
///         el sistema. Evita que se elimine la única vía de acceso
///         super-admin.</item>
///   <item>Publica <see cref="UsuarioRolRevocadoEvent"/> via
///         <see cref="IIntegrationEventPublisher"/>.</item>
/// </list>
/// </summary>
public sealed record RevocarRolDeUsuarioCommand(
    Guid UsuarioEmpresaRolId) : IRequest<Unit>;

public sealed class RevocarRolDeUsuarioHandler
    : IRequestHandler<RevocarRolDeUsuarioCommand, Unit>
{
    /// <summary>Código del rol super-admin (alineado con bootstrap).</summary>
    public const string SuperAdminCodigo = "super-admin";

    private readonly IdentidadDbContext _db;
    private readonly IIntegrationEventPublisher _events;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IClock _clock;

    public RevocarRolDeUsuarioHandler(
        IdentidadDbContext db,
        IIntegrationEventPublisher events,
        ICurrentEmpresaContext empresaContext,
        IClock clock)
    {
        _db = db;
        _events = events;
        _empresaContext = empresaContext;
        _clock = clock;
    }

    public async Task<Unit> Handle(
        RevocarRolDeUsuarioCommand command, CancellationToken cancellationToken)
    {
        // Revocar es cross-empresa por la misma razón que asignar (ver
        // AsignarRolAUsuarioHandler). Sin bypass, el SELECT del query
        // filter oculta asignaciones de otras empresas y el DELETE
        // (que va por SaveChanges) levanta CROSS_EMPRESA_VIOLATION.
        // Endpoint gated por identidad.asignaciones.administrar (RBAC).
        using var bypass = _empresaContext.Bypass();

        var asignacion = await _db.UsuarioEmpresaRoles
            .FirstOrDefaultAsync(
                uer => uer.Id == command.UsuarioEmpresaRolId,
                cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_ASIGNACION_NO_ENCONTRADA",
                $"No existe asignación usuario↔empresa↔rol con id '{command.UsuarioEmpresaRolId}'.");

        await EnsureNoEsUltimaAsignacionDeSuperAdminAsync(asignacion.RolId, cancellationToken);

        var (usuarioId, empresaId, rolId) =
            (asignacion.UsuarioId, asignacion.EmpresaId, asignacion.RolId);

        _db.UsuarioEmpresaRoles.Remove(asignacion);
        await _db.SaveChangesAsync(cancellationToken);

        await _events.PublishAsync(
            new UsuarioRolRevocadoEvent(
                usuarioId,
                empresaId,
                rolId,
                _clock.UtcNow),
            cancellationToken);

        return Unit.Value;
    }

    /// <summary>
    /// Si la asignación a borrar es del rol super-admin Y es la única
    /// asignación de ese rol a un usuario activo, lanza 422.
    /// </summary>
    private async Task EnsureNoEsUltimaAsignacionDeSuperAdminAsync(
        Guid rolId, CancellationToken ct)
    {
        var rol = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == rolId, ct);
        if (rol is null || rol.Codigo != SuperAdminCodigo) return;

        // ¿Cuántas asignaciones de super-admin a usuarios activos hay en
        // total? Si solo queda 1, esa 1 es la que vamos a borrar.
        var totalAsignacionesActivas = await (
            from uer in _db.UsuarioEmpresaRoles.AsNoTracking()
            join u in _db.Usuarios.AsNoTracking() on uer.UsuarioId equals u.Id
            where uer.RolId == rolId && u.Activo
            select uer.Id)
            .CountAsync(ct);

        if (totalAsignacionesActivas <= 1)
        {
            throw new BusinessRuleException(
                "USUARIO_ULTIMO_SUPER_ADMIN",
                "No se puede revocar la única asignación activa del rol super-admin. " +
                "Asigne super-admin a otro usuario activo antes de revocar ésta.");
        }
    }
}
