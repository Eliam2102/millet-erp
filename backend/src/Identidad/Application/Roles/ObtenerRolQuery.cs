using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Detalle de un rol con sus permisos y grupos Entra ID asociados
/// (F-Admin-PR3.2). 404 si no existe.
/// </summary>
public sealed record ObtenerRolQuery(Guid Id) : IRequest<RolDetalleResponse>;

public sealed class ObtenerRolHandler
    : IRequestHandler<ObtenerRolQuery, RolDetalleResponse>
{
    private readonly IdentidadDbContext _db;

    public ObtenerRolHandler(IdentidadDbContext db) => _db = db;

    public async Task<RolDetalleResponse> Handle(
        ObtenerRolQuery query, CancellationToken cancellationToken)
    {
        var rol = await _db.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ROL_NO_ENCONTRADO",
                $"No existe rol con id '{query.Id}'.");

        var permisoIds = await _db.RolPermisos.AsNoTracking()
            .Where(rp => rp.RolId == query.Id)
            .Select(rp => rp.PermisoId)
            .ToListAsync(cancellationToken);

        var grupos = await _db.RolGruposEntraId.AsNoTracking()
            .Where(rge => rge.RolId == query.Id)
            .OrderBy(rge => rge.Nombre)
            .Select(rge => new RolGrupoEntraIdResponse(
                rge.Id, rge.RolId, rge.ObjectId, rge.Nombre))
            .ToListAsync(cancellationToken);

        var rolDto = new RolResponse(
            rol.Id, rol.Codigo, rol.Nombre, rol.Descripcion,
            rol.EsDelSistema, rol.Activo, rol.Version);

        return new RolDetalleResponse(rolDto, permisoIds, grupos);
    }
}
