using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    public static bool PermisoDelRol(string rol, string codigo)
    {
        if (rol == "Administrador") return true;
        // Ningún perfil operativo gana bypass territorial, identidad ni administración
        // de roles por el hecho de tener acceso a su módulo.
        if (codigo.Contains("todas-sucursales", StringComparison.Ordinal)) return false;
        if (codigo.StartsWith("compartido.catalogos.", StringComparison.Ordinal) && codigo.EndsWith(".leer", StringComparison.Ordinal)) return true;
        if (codigo is "admin.sucursales.leer" or "admin.departamentos.leer") return true;
        return rol switch
        {
            "Compras" => codigo.StartsWith("compras.", StringComparison.Ordinal)
                || codigo.StartsWith("almacen.", StringComparison.Ordinal)
                || codigo is "datos_maestros.proveedores.leer" or "datos_maestros.articulos.leer",
            "CxP" => codigo.StartsWith("cuentas_por_pagar.", StringComparison.Ordinal)
                || codigo.StartsWith("datos_maestros.proveedores.", StringComparison.Ordinal),
            "Tesorería" => codigo.StartsWith("tesoreria.", StringComparison.Ordinal),
            "Facturación" => codigo.StartsWith("facturacion.", StringComparison.Ordinal)
                || codigo.StartsWith("datos_maestros.clientes.", StringComparison.Ordinal),
            "Contabilidad" => codigo.StartsWith("contabilidad.", StringComparison.Ordinal),
            _ => false,
        };
    }

    private async Task SembrarUsuariosAsync(IServiceProvider sp, DemoSesionUsuario[] usuarios, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentidadDbContext>();
        var sucursales = await sp.GetRequiredService<CompartidoDbContext>().Sucursales
            .Where(s => s.EmpresaId == EmpresaId).ToDictionaryAsync(s => s.Clave, ct);
        foreach (var nombre in new[] { "Compras", "CxP", "Tesorería", "Facturación", "Contabilidad", "Administrador" })
        {
            var codigo = "DEMO-ROL-" + nombre;
            var rol = await db.Roles.SingleOrDefaultAsync(r => r.Codigo == codigo, ct);
            if (rol is null)
            {
                rol = new Rol(Id(codigo), codigo, "DEMO " + nombre, descripcion: "Perfil ficticio de la sesión del 12-oct.");
                db.Roles.Add(rol);
            }
            var permisos = PermisosCanonicos.Todos.Where(p => PermisoDelRol(nombre, p.Codigo)).Select(p => p.Id).ToHashSet();
            var actuales = await db.RolPermisos.Where(p => p.RolId == rol.Id).ToListAsync(ct);
            db.RolPermisos.RemoveRange(actuales.Where(p => !permisos.Contains(p.PermisoId)));
            foreach (var permiso in permisos.Except(actuales.Select(p => p.PermisoId)))
                db.RolPermisos.Add(new RolPermiso(Id($"{codigo}/{permiso}"), rol.Id, permiso));
        }
        await db.SaveChangesAsync(ct);

        foreach (var entrada in usuarios)
        {
            var correo = entrada.Correo.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            var usuario = await db.Usuarios.SingleOrDefaultAsync(u => EF.Functions.ILike(u.Email, correo, "\\"), ct);
            if (usuario is null)
            {
                logger.LogWarning("DEMO: usuario {Correo} aún no existe. Por confirmar: iniciar sesión con Entra y reiniciar.", entrada.Correo);
                continue;
            }
            if (!usuario.Activo || usuario.TieneOidPendiente)
                throw new InvalidOperationException($"DEMO: {entrada.Correo} debe ser un usuario activo con identidad resuelta.");
            var rolId = Id("DEMO-ROL-" + entrada.Rol);
            var asignaciones = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuario.Id && a.EmpresaId == EmpresaId).ToListAsync(ct);
            // Configuración autoritativa para estas cuentas DEMO: evita conservar
            // super-admin y que la escena de rechazo de MTY produzca un falso éxito.
            db.UsuarioEmpresaRoles.RemoveRange(asignaciones.Where(a => a.RolId != rolId));
            if (!asignaciones.Any(a => a.RolId == rolId))
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Id($"DEMO-ASIGNACION/{usuario.Id}/{rolId}"), usuario.Id, EmpresaId, rolId));
            db.UsuarioPermisoOverrides.RemoveRange(await db.UsuarioPermisoOverrides
                .Where(a => a.UsuarioId == usuario.Id && a.EmpresaId == EmpresaId).ToListAsync(ct));
            var deseadas = entrada.Sucursales.Select(clave => sucursales.TryGetValue(clave, out var s)
                ? s.Id : throw new InvalidOperationException($"DEMO: falta la sucursal canónica {clave}.")).ToHashSet();
            var alcances = await db.UsuarioSucursales.Where(a => a.UsuarioId == usuario.Id && a.EmpresaId == EmpresaId).ToListAsync(ct);
            foreach (var alcance in alcances)
            {
                if (deseadas.Contains(alcance.SucursalId)) alcance.Activar();
                else alcance.Desactivar();
            }
            foreach (var sucursalId in deseadas.Except(alcances.Select(a => a.SucursalId)))
                db.UsuarioSucursales.Add(new UsuarioSucursal(Id($"DEMO-SUCURSAL/{usuario.Id}/{sucursalId}"), usuario.Id, sucursalId, EmpresaId));
            await db.SaveChangesAsync(ct);
        }
    }
}
