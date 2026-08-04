using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Implementación de <see cref="IPermissionLoader"/> que arma el set efectivo
/// de permisos vía join Usuario → UsuarioEmpresaRol → Rol → RolPermiso →
/// Permiso, filtrando por <c>Usuario.Activo</c> y <c>Rol.Activo</c>.
///
/// <para>
/// Usa <see cref="ICurrentEmpresaContext.Bypass"/> alrededor de la query
/// porque cuando el global query filter por empresa exista (PR 4),
/// <c>UsuarioEmpresaRoles</c> quedará filtrado por la empresa actual del
/// claim — pero acá estamos consultando los permisos PARA esa empresa
/// específica que viene como parámetro, no necesariamente la del claim.
/// </para>
/// </summary>
public sealed class PermissionLoader : IPermissionLoader
{
    private readonly IdentidadDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;

    public PermissionLoader(IdentidadDbContext db, ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _empresaContext = empresaContext;
    }

    public async Task<IReadOnlyCollection<string>> LoadForUserInEmpresaAsync(
        Guid userId,
        Guid empresaId,
        CancellationToken cancellationToken = default)
    {
        using var bypass = _empresaContext.Bypass();

        var permisos = await (
            from u in _db.Usuarios
            where u.Id == userId && u.Activo
            join uer in _db.UsuarioEmpresaRoles on u.Id equals uer.UsuarioId
            where uer.EmpresaId == empresaId
            join r in _db.Roles on uer.RolId equals r.Id
            where r.Activo
            join rp in _db.RolPermisos on r.Id equals rp.RolId
            join p in _db.Permisos on rp.PermisoId equals p.Id
            select p.Codigo)
            .Distinct()
            .ToListAsync(cancellationToken);

        return permisos;
    }
}
