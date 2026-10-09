using Millet.SharedKernel.Application;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.Roles;

internal static class RolPermissionCache
{
    public static async Task InvalidarAsync(Guid rolId, IdentidadDbContext db,
        IPermissionCache cache, CancellationToken ct)
    {
        // LOWER es traducible por PostgreSQL; los ObjectId reales de Entra son GUID ASCII.
#pragma warning disable CA1304, CA1311
        var usuariosGrupo = from ug in db.UsuarioGruposEntraId
                            join rg in db.RolGruposEntraId on ug.ObjectId.ToLower() equals rg.ObjectId.ToLower()
                            where rg.RolId == rolId
                            select ug.UsuarioId;
#pragma warning restore CA1304, CA1311
        var usuarios = await db.UsuarioEmpresaRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.RolId == rolId || usuariosGrupo.Contains(x.UsuarioId))
            .Select(x => new { x.UsuarioId, x.EmpresaId }).Distinct().ToListAsync(ct);
        foreach (var usuario in usuarios)
            await cache.InvalidateAsync(usuario.UsuarioId, usuario.EmpresaId, ct);
    }
}
