using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Identidad.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter de <see cref="IDestinatariosAvisoReadPort"/> (86e3tac2y): usuarios activos con el permiso por rol directo o por grupo de
/// Entra, más las excepciones concedidas y menos las denegadas (misma regla que <see cref="PermissionLoader"/>). Con sucursal, solo
/// los asignados a ella o con alcance corporativo.
/// </summary>
public sealed class DestinatariosAvisoReadAdapter(IdentidadDbContext db, ICurrentEmpresaContext empresaContext) : IDestinatariosAvisoReadPort
{
    public async Task<IReadOnlyList<Guid>> UsuariosConPermisoAsync(Guid empresaId, string permiso, Guid? sucursalId, CancellationToken cancellationToken)
    {
        using var bypass = empresaContext.Bypass();
        var permisoId = db.Permisos.Where(p => p.Codigo == permiso).Select(p => p.Id);
        var roles = (from rp in db.RolPermisos
                     join r in db.Roles on rp.RolId equals r.Id
                     where r.Activo && permisoId.Contains(rp.PermisoId)
                     select r.Id).ToList();
        var directos = db.UsuarioEmpresaRoles.Where(x => x.EmpresaId == empresaId);
        var porRol = directos.Where(x => roles.Contains(x.RolId)).Select(x => x.UsuarioId);
#pragma warning disable CA1304, CA1311
        var porGrupo = from ug in db.UsuarioGruposEntraId
                       join rg in db.RolGruposEntraId on ug.ObjectId.ToLower() equals rg.ObjectId.ToLower()
                       where roles.Contains(rg.RolId)
                       select ug.UsuarioId;
#pragma warning restore CA1304, CA1311
        var overrides = db.UsuarioPermisoOverrides.Where(o => o.EmpresaId == empresaId && permisoId.Contains(o.PermisoId));
        var concedidos = overrides.Where(o => o.Efecto == EfectoPermiso.Conceder).Select(o => o.UsuarioId);
        var denegados = overrides.Where(o => o.Efecto == EfectoPermiso.Denegar).Select(o => o.UsuarioId);

        // Los grupos y las excepciones solo cuentan para quien ya tiene algún rol directo en la empresa.
        var candidatos = porRol.Union(porGrupo.Where(u => directos.Any(d => d.UsuarioId == u)))
            .Union(concedidos.Where(u => directos.Any(d => d.UsuarioId == u)));
        var q = db.Usuarios.Where(u => u.Activo && candidatos.Contains(u.Id) && !denegados.Contains(u.Id));
        if (sucursalId is { } sid)
        {
            var corporativos = db.UsuarioEmpresaAlcances.Where(a => a.EmpresaId == empresaId && a.EsCorporativo).Select(a => a.UsuarioId);
            var asignados = db.UsuarioSucursales.Where(a => a.SucursalId == sid && a.Estatus == EstatusCatalogo.Activo).Select(a => a.UsuarioId);
            q = q.Where(u => corporativos.Contains(u.Id) || asignados.Contains(u.Id));
        }
        return await q.Select(u => u.Id).ToListAsync(cancellationToken);
    }
}
