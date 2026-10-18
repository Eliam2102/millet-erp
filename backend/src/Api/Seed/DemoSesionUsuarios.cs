using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;

namespace Millet.Api.Seed;

public sealed partial class DemoSesionSeedHostedService
{
    public static readonly Guid CapturistaComprasDemoId = Id("DEMO-USUARIO-CAPTURISTA-COMPRAS");
    public static readonly Guid JefeComprasDemoId = Id("DEMO-USUARIO-JEFE-COMPRAS");
    public static readonly Guid DireccionDemoId = Id("DEMO-USUARIO-DIRECCION");

    public static bool PermisoDelRol(string rol, string codigo)
    {
        if (rol == "Jefe Compras y CxP") return PermisoDelRol("Jefe Compras", codigo) || PermisoDelRol("CxP", codigo);
        if (rol == "Dirección y Tesorería") return PermisoDelRol("Dirección", codigo) || PermisoDelRol("Tesorería", codigo);
        if (rol == "Administrador") return true;
        if (rol is "Capturista Compras" or "Jefe Compras" or "Dirección")
        {
            if (codigo == PermisosCanonicos.ComprasOrdenesAutorizarNivel1)
                return rol == "Jefe Compras";
            if (codigo == PermisosCanonicos.ComprasOrdenesAutorizarNivel2)
                return rol == "Dirección";
            return PermisoDelRol("Compras", codigo);
        }
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
            "Contabilidad" => codigo.StartsWith("contabilidad.", StringComparison.Ordinal)
                && codigo != PermisosCanonicos.ContabilidadCatalogoAutorizar,
            "DAF" => codigo is PermisosCanonicos.ContabilidadCatalogoLeer or PermisosCanonicos.ContabilidadCatalogoAutorizar,
            _ => false,
        };
    }

    private async Task SembrarUsuariosAsync(IServiceProvider sp, DemoSesionUsuario[] usuarios, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentidadDbContext>();
        var reemplazar = environment.IsDevelopment()
            && configuration.GetValue<bool>("Seed:DemoSesion:Habilitado")
            && configuration.GetValue<bool>("Seed:DemoSesion:ReemplazarAsignaciones");
        if (reemplazar)
        {
            var correosAdmin = await (from asignacion in db.UsuarioEmpresaRoles
                join rol in db.Roles on asignacion.RolId equals rol.Id
                join usuario in db.Usuarios on asignacion.UsuarioId equals usuario.Id
                where asignacion.EmpresaId == EmpresaId && rol.Codigo == "super-admin"
                select usuario.Email).ToListAsync(ct);
            if (configuration["Auth:InitialAdminEntraOid"]?.Trim() is { Length: > 0 } adminOid)
            {
                var oidLiteral = PatronLiteral(adminOid);
                correosAdmin.AddRange(await db.Usuarios.Where(u => EF.Functions.ILike(u.EntraOid, oidLiteral, "\\"))
                    .Select(u => u.Email).ToArrayAsync(ct));
            }
            if (usuarios.Any(u => correosAdmin.Contains(u.Correo.Trim(), StringComparer.OrdinalIgnoreCase)))
                throw new InvalidOperationException("DEMO: el administrador queda fuera de las cuentas del ensayo (super-admin en la empresa DEMO o Auth:InitialAdminEntraOid); use tres cuentas operativas aparte.");
        }
        var sucursales = await sp.GetRequiredService<CompartidoDbContext>().Sucursales
            .Where(s => s.EmpresaId == EmpresaId).ToDictionaryAsync(s => s.Clave, ct);
        foreach (var nombre in new[] { "Compras", "CxP", "Tesorería", "Facturación", "Contabilidad", "DAF", "Administrador",
            "Capturista Compras", "Jefe Compras", "Dirección", "Jefe Compras y CxP", "Dirección y Tesorería" })
        {
            var codigo = "DEMO-ROL-" + nombre;
            var rol = await db.Roles.SingleOrDefaultAsync(r => r.Codigo == codigo, ct);
            if (rol is null)
            {
                rol = new Rol(Id(codigo), codigo, "DEMO " + nombre, descripcion: "Perfil ficticio de la sesión del 12-oct.");
                db.Roles.Add(rol);
            }
            // Los permisos iniciales se crean junto al rol; los cambios del administrador se conservan.
            if (db.Entry(rol).State == EntityState.Added)
                foreach (var permiso in PermisosCanonicos.Todos.Where(p => PermisoDelRol(nombre, p.Codigo)))
                    db.RolPermisos.Add(new RolPermiso(Id($"{codigo}/{permiso.Id}"), rol.Id, permiso.Id));
        }
        await db.SaveChangesAsync(ct);

        // Identidades ficticias locales de la sesión; no provisiona cuentas en Entra.
        // Separarlas del actor técnico conserva las reglas P2 al sembrar las OCs.
        foreach (var (id, clave, nombre, rol) in new[]
        {
            (CapturistaComprasDemoId, "capturista-compras", "DEMO Capturista Compras", "Capturista Compras"),
            (JefeComprasDemoId, "jefe-compras", "DEMO Jefe de Compras", "Jefe Compras"),
            (DireccionDemoId, "direccion", "DEMO Dirección", "Dirección"),
            (CajeraDemoId, "cajera-mid", "DEMO Cajera Mostrador Conkal", "Facturación"),
        })
        {
            if (!await db.Usuarios.AnyAsync(u => u.Id == id, ct))
                db.Usuarios.Add(new Usuario(id, "demo-sesion-" + clave,
                    clave + "@example.invalid", nombre, esCuentaTecnica: true));
            var rolId = Id("DEMO-ROL-" + rol);
            if (!await db.UsuarioEmpresaRoles.AnyAsync(a => a.UsuarioId == id && a.EmpresaId == EmpresaId && a.RolId == rolId, ct))
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Id($"DEMO-ASIGNACION/{id}/{rolId}"), id, EmpresaId, rolId));
            // Capturista y cajera operan solo en MID (Conkal); el resto ve todas las sucursales.
            foreach (var sucursal in sucursales.Values.Where(s => rol is not ("Capturista Compras" or "Facturación") || s.Clave == "MID"))
            {
                if (!await db.UsuarioSucursales.AnyAsync(a => a.UsuarioId == id && a.EmpresaId == EmpresaId && a.SucursalId == sucursal.Id, ct))
                    db.UsuarioSucursales.Add(new UsuarioSucursal(Id($"DEMO-SUCURSAL/{id}/{sucursal.Id}"), id, sucursal.Id, EmpresaId));
            }
        }
        await db.SaveChangesAsync(ct);

        var codigosRoles = await db.Roles.ToDictionaryAsync(r => r.Id, r => r.Codigo, ct);
        foreach (var entrada in usuarios)
        {
            var correo = PatronLiteral(entrada.Correo.Trim());
            var usuario = await db.Usuarios.SingleOrDefaultAsync(u => EF.Functions.ILike(u.Email, correo, "\\"), ct);
            if (usuario is null)
            {
                logger.LogWarning("DEMO: usuario {Correo} aún no existe. Por confirmar: iniciar sesión con Entra y reiniciar.", entrada.Correo);
                continue;
            }
            if (!usuario.Activo || usuario.TieneOidPendiente)
                throw new InvalidOperationException($"DEMO: {entrada.Correo} debe ser un usuario activo con identidad resuelta.");
            var rolDeseado = Id("DEMO-ROL-" + entrada.Rol);
            var sucursalesDeseadas = entrada.Sucursales.Select(clave => sucursales.TryGetValue(clave, out var sucursal) ? sucursal.Id
                : throw new InvalidOperationException($"DEMO: falta la sucursal canónica {clave}.")).ToArray();
            var asignaciones = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuario.Id && a.EmpresaId == EmpresaId).ToListAsync(ct);
            var alcance = await db.UsuarioSucursales.Where(a => a.UsuarioId == usuario.Id && a.EmpresaId == EmpresaId).ToListAsync(ct);
            var rolesAntes = asignaciones.Select(a => codigosRoles[a.RolId]).Order().ToArray();
            var sucursalesAntes = alcance.Where(a => a.Estatus == EstatusCatalogo.Activo)
                .Select(a => sucursales.Values.Single(s => s.Id == a.SucursalId).Clave).Order().ToArray();
            if (reemplazar)
            {
                // ADR-0053: una sola asignación por usuario y empresa; administradores excluidos arriba.
                db.UsuarioEmpresaRoles.RemoveRange(asignaciones.Where(a => a.RolId != rolDeseado));
                foreach (var asociacion in alcance)
                {
                    if (sucursalesDeseadas.Contains(asociacion.SucursalId)) asociacion.Activar();
                    else asociacion.Desactivar();
                }
            }
            // Sin bandera solo completa cuentas sin roles o sin alcance, como antes.
            if ((reemplazar || asignaciones.Count == 0) && asignaciones.All(a => a.RolId != rolDeseado))
                db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Id($"DEMO-ASIGNACION/{usuario.Id}/{rolDeseado}"), usuario.Id, EmpresaId, rolDeseado));
            if (reemplazar || alcance.Count == 0)
                foreach (var sucursalId in sucursalesDeseadas.Where(id => alcance.All(a => a.SucursalId != id)))
                    db.UsuarioSucursales.Add(new UsuarioSucursal(Id($"DEMO-SUCURSAL/{usuario.Id}/{sucursalId}"), usuario.Id, sucursalId, EmpresaId));
            await db.SaveChangesAsync(ct);
            if (reemplazar)
            {
                var rolesDespues = await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == usuario.Id && a.EmpresaId == EmpresaId)
                    .Select(a => a.RolId).ToArrayAsync(ct);
                logger.LogInformation("DEMO: {Correo}, roles {RolesAntes} → {RolesDespues}; sucursales {SucursalesAntes} → {SucursalesDespues}",
                    entrada.Correo, string.Join(", ", rolesAntes), string.Join(", ", rolesDespues.Select(id => codigosRoles[id]).Order()),
                    string.Join(", ", sucursalesAntes), string.Join(", ", entrada.Sucursales.Order()));
            }
        }
    }

    private static string PatronLiteral(string valor) => valor.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
