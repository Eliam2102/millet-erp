using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// "Elimina" un rol (F-Admin-PR3.2). Decisión MVP: soft-delete via
/// <c>Rol.Desactivar()</c> (setea <c>Activo=false</c>), no hard-delete.
/// Razones:
/// <list type="bullet">
///   <item>Preserva trazabilidad: usuarios que tenían el rol siguen viéndolo
///         en auditoría aunque ya no autoriza nada.</item>
///   <item>Evita cascade de RolPermisos y RolGruposEntraId. Las
///         asignaciones a usuarios (<c>UsuarioEmpresaRol</c>) tienen FK
///         <c>Restrict</c>: hard-delete fallaría si el rol está asignado.</item>
/// </list>
/// El dominio rechaza desactivar roles del sistema (422).
/// Idempotente: si ya está inactivo, no-op.
/// </summary>
public sealed record EliminarRolCommand(Guid Id) : IRequest<Unit>;

public sealed class EliminarRolHandler : IRequestHandler<EliminarRolCommand, Unit>
{
    private readonly IdentidadDbContext _db;

    public EliminarRolHandler(IdentidadDbContext db) => _db = db;

    public async Task<Unit> Handle(
        EliminarRolCommand command, CancellationToken cancellationToken)
    {
        var rol = await _db.Roles
            .FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ROL_NO_ENCONTRADO",
                $"No existe rol con id '{command.Id}'.");

        if (rol.EsDelSistema)
        {
            throw new BusinessRuleException(
                "ROL_DEL_SISTEMA_NO_ELIMINABLE",
                "Los roles del sistema (super-admin, etc.) no se pueden eliminar.");
        }

        if (rol.Activo)
        {
            rol.Desactivar();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
