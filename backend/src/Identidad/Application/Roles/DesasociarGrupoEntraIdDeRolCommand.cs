using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Borra una asociación <c>RolGrupoEntraId</c> por id (F-Admin-PR3.2).
/// 404 si no existe.
/// </summary>
public sealed record DesasociarGrupoEntraIdDeRolCommand(
    Guid RolGrupoEntraIdId) : IRequest<Unit>;

public sealed class DesasociarGrupoEntraIdDeRolHandler
    : IRequestHandler<DesasociarGrupoEntraIdDeRolCommand, Unit>
{
    private readonly IdentidadDbContext _db;

    public DesasociarGrupoEntraIdDeRolHandler(IdentidadDbContext db) => _db = db;

    public async Task<Unit> Handle(
        DesasociarGrupoEntraIdDeRolCommand command, CancellationToken cancellationToken)
    {
        var asociacion = await _db.RolGruposEntraId
            .FirstOrDefaultAsync(rge => rge.Id == command.RolGrupoEntraIdId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ROL_GRUPO_ENTRAID_NO_ENCONTRADO",
                $"No existe asociación rol↔grupo Entra ID con id '{command.RolGrupoEntraIdId}'.");

        _db.RolGruposEntraId.Remove(asociacion);
        await _db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
