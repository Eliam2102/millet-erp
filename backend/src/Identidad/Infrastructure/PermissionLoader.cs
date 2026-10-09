using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Application;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Implementación de <see cref="IPermissionLoader"/> que arma el set efectivo
/// de permisos vía join Usuario → UsuarioEmpresaRol → Rol → RolPermiso →
/// Permiso, filtrando por <c>Usuario.Activo</c> y <c>Rol.Activo</c>, y le
/// aplica las excepciones por usuario/empresa (<see cref="UsuarioPermisoOverride"/>,
/// ADR-0053): <c>efectivos = (rol ∪ Conceder) \ Denegar</c>.
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

        var rolesDirectos = _db.UsuarioEmpresaRoles
            .Where(x => x.UsuarioId == userId && x.EmpresaId == empresaId).Select(x => x.RolId);
        // LOWER es traducible por PostgreSQL; los ObjectId reales de Entra son GUID ASCII.
#pragma warning disable CA1304, CA1311
        var rolesGrupos = from ug in _db.UsuarioGruposEntraId
                          where ug.UsuarioId == userId
                          join rg in _db.RolGruposEntraId on ug.ObjectId.ToLower() equals rg.ObjectId.ToLower()
                          select rg.RolId;
#pragma warning restore CA1304, CA1311
        var rolesEfectivos = rolesDirectos.Union(rolesGrupos);
        // Los grupos suman roles dentro de empresas previamente asignadas; nunca dan acceso a otra empresa.
        if (!await _db.Usuarios.AnyAsync(u => u.Id == userId && u.Activo, cancellationToken)
            || !await rolesDirectos.AnyAsync(cancellationToken)
            || !await _db.Roles.AnyAsync(r => r.Activo && rolesEfectivos.Contains(r.Id), cancellationToken))
            return Array.Empty<string>();

        var permisos = await (
            from r in _db.Roles
            where r.Activo && rolesEfectivos.Contains(r.Id)
            join rp in _db.RolPermisos on r.Id equals rp.RolId
            join p in _db.Permisos on rp.PermisoId equals p.Id
            select p.Codigo)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Excepciones por usuario/empresa: efectivos = (rol ∪ Conceder) \ Denegar.
        var overrides = await (
            from o in _db.UsuarioPermisoOverrides
            where o.UsuarioId == userId && o.EmpresaId == empresaId
            join p in _db.Permisos on o.PermisoId equals p.Id
            select new { p.Codigo, o.Efecto })
            .ToListAsync(cancellationToken);


        var efectivos = new HashSet<string>(permisos, StringComparer.Ordinal);
        foreach (var o in overrides.Where(o => o.Efecto == EfectoPermiso.Conceder))
        {
            efectivos.Add(o.Codigo);
        }
        foreach (var o in overrides.Where(o => o.Efecto == EfectoPermiso.Denegar))
        {
            efectivos.Remove(o.Codigo); // Deny gana sobre Conceder.
        }

        var esSuperAdmin = await _db.Roles.AnyAsync(r => r.Activo && r.Codigo == "super-admin" && rolesEfectivos.Contains(r.Id), cancellationToken);
        if (!esSuperAdmin)
        {
            efectivos.Remove(PermisosCanonicos.AdminEmpresasCrear);
            efectivos.Remove(PermisosCanonicos.AdminEmpresasDesactivar);
        }
        return efectivos.ToList();
    }
}
