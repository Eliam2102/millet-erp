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

        // Usuario activo con al menos un rol activo en la empresa. Sin esto no
        // hay permisos, ni siquiera "concedidos" (las excepciones se montan
        // sobre la base del rol, ADR-0053).
        var tieneRolActivo = await (
            from u in _db.Usuarios
            where u.Id == userId && u.Activo
            join uer in _db.UsuarioEmpresaRoles on u.Id equals uer.UsuarioId
            where uer.EmpresaId == empresaId
            join r in _db.Roles on uer.RolId equals r.Id
            where r.Activo
            select r.Id)
            .AnyAsync(cancellationToken);

        if (!tieneRolActivo)
        {
            return Array.Empty<string>();
        }

        var permisos = await (
            from uer in _db.UsuarioEmpresaRoles
            where uer.UsuarioId == userId && uer.EmpresaId == empresaId
            join r in _db.Roles on uer.RolId equals r.Id
            where r.Activo
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

        if (overrides.Count == 0)
        {
            return permisos;
        }

        var efectivos = new HashSet<string>(permisos, StringComparer.Ordinal);
        foreach (var o in overrides.Where(o => o.Efecto == EfectoPermiso.Conceder))
        {
            efectivos.Add(o.Codigo);
        }
        foreach (var o in overrides.Where(o => o.Efecto == EfectoPermiso.Denegar))
        {
            efectivos.Remove(o.Codigo); // Deny gana sobre Conceder.
        }

        return efectivos.ToList();
    }
}
