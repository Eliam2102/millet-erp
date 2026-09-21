using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Identidad.Infrastructure;

/// <summary>
/// Hosted service que crea el primer SuperAdmin en arranque del API si no
/// existe (ADR-0007). Idempotente: re-arranques posteriores detectan al
/// usuario y verifican que el rol y los permisos estén completos sin
/// duplicar nada.
///
/// <para>Pasos (cada uno condicional):</para>
/// <list type="number">
///   <item>Si <c>Auth:InitialAdminEntraOid</c> está vacío → omite todo</item>
///   <item>Asegura el rol "super-admin" con TODOS los permisos canónicos</item>
///   <item>Crea el Usuario + UsuarioPreferencia si no existe</item>
///   <item>Si <c>Auth:Bootstrap:EmpresaInicial:Rfc</c> está set, crea la
///         empresa inicial (en CompartidoDbContext) y asigna al SuperAdmin
///         a esa empresa con el rol super-admin</item>
/// </list>
///
/// <para>
/// Cross-context: empresa va a <c>compartido</c>, asignación va a
/// <c>identidad</c>. Sin transacción global (EF Core no soporta cross-context).
/// Orden: compartido primero (FK target), luego identidad. Re-arranques
/// toleran fallos parciales gracias a la idempotencia.
/// </para>
///
/// <para>
/// Bypass del global query filter por empresa (PR 4) durante TODA la
/// operación: el SuperAdmin no tiene asignación todavía, así que sin
/// bypass las queries no devolverían nada útil.
/// </para>
/// </summary>
public sealed class BootstrapSuperAdminHostedService : IHostedService
{
    internal const long BootstrapLockId = 6_672_000_010;

    // IDs deterministas: idempotencia robusta y trazables en logs/audit_log.
    private static readonly Guid SuperAdminRolId = Guid.Parse("00000002-0003-0000-0000-000000000001");
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string SuperAdminCodigo = "super-admin";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BootstrapSuperAdminOptions> _options;
    private readonly ILogger<BootstrapSuperAdminHostedService> _logger;

    public BootstrapSuperAdminHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BootstrapSuperAdminOptions> options,
        ILogger<BootstrapSuperAdminHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var oid = _options.Value.InitialAdminEntraOid?.Trim();
        if (string.IsNullOrEmpty(oid))
        {
            _logger.LogInformation(
                "Auth:InitialAdminEntraOid no configurado. Bootstrap del SuperAdmin omitido.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var compartido = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var originContext = scope.ServiceProvider.GetRequiredService<IAuditOriginContext>();
        using var origin = originContext.SetOrigin(nameof(BootstrapSuperAdminHostedService));
        using var bypass = empresaContext.Bypass();

        await PostgresAdvisoryLock.ExecuteAsync(
            identidad,
            BootstrapLockId,
            async ct =>
            {
                var rol = await EnsureSuperAdminRolAsync(identidad, ct);
                await EnsureRolesMvpAsync(identidad, ct);
                var usuario = await EnsureSuperAdminUsuarioAsync(identidad, oid, ct);
                await EnsureEmpresaAndAssignmentAsync(identidad, compartido, usuario, rol, ct);
            },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Crea el Rol "super-admin" si no existe y le asigna todos los
    /// permisos de <see cref="PermisosCanonicos.Todos"/>. Idempotente.
    /// </summary>
    private async Task<Rol> EnsureSuperAdminRolAsync(
        IdentidadDbContext db,
        CancellationToken cancellationToken)
    {
        var rol = await db.Roles
            .FirstOrDefaultAsync(r => r.Codigo == SuperAdminCodigo, cancellationToken);

        if (rol is null)
        {
            rol = new Rol(
                SuperAdminRolId,
                SuperAdminCodigo,
                "SuperAdmin",
                esDelSistema: true,
                "Acceso total al sistema (todos los permisos canónicos). Creado por bootstrap.");
            db.Roles.Add(rol);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Rol 'super-admin' creado con id={RolId}", rol.Id);
        }

        var existingPermisoIds = await db.RolPermisos
            .Where(rp => rp.RolId == rol.Id)
            .Select(rp => rp.PermisoId)
            .ToListAsync(cancellationToken);

        var allPermisoIds = PermisosCanonicos.Todos.Select(p => p.Id).ToHashSet();
        var missing = allPermisoIds.Except(existingPermisoIds).ToList();

        if (missing.Count > 0)
        {
            foreach (var permisoId in missing)
            {
                db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rol.Id, permisoId));
            }
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Asignados {Count} permisos canónicos al rol super-admin (total: {Total})",
                missing.Count,
                allPermisoIds.Count);
        }

        return rol;
    }

    /// <summary>
    /// Seed de los 6 roles MVP adicionales al super-admin (F-Admin-PR3.3,
    /// A2 cerrada 2026-05-13). Cada rol agrupa un subset de permisos
    /// canónicos por área de responsabilidad:
    ///
    /// <list type="bullet">
    ///   <item><b>Administrador de identidad</b>: <c>identidad.*</c></item>
    ///   <item><b>Administrador organizacional</b>: <c>admin.empresas.*</c>, <c>admin.departamentos.*</c></item>
    ///   <item><b>Administrador de catálogos</b>: <c>compartido.catalogos.*</c></item>
    ///   <item><b>Administrador de datos maestros</b>: <c>compartido.catalogos.*</c> (Proveedor/Artículo viven en catálogos hoy)</item>
    ///   <item><b>Auditor</b>: <c>admin.auditoria.leer</c> + <c>infra.audit_log.leer</c> + todos los <c>.leer</c> del sistema (read-only)</item>
    ///   <item><b>Administrador Compras</b>: <c>compras.configuracion.*</c></item>
    /// </list>
    ///
    /// Idempotente: usa GUIDs deterministas y verifica existencia antes
    /// de insertar. Cada role tiene <see cref="Rol.EsDelSistema"/>=true
    /// (no se pueden editar/eliminar por endpoints).
    /// </summary>
    private async Task EnsureRolesMvpAsync(
        IdentidadDbContext db,
        CancellationToken cancellationToken)
    {
        // Definición declarativa: (RolId, Codigo, Nombre, Descripcion, Predicate para filtrar permisos).
        var rolesDefinicion = new (Guid RolId, string Codigo, string Nombre, string Descripcion, Func<(Guid Id, string Codigo, string Descripcion), bool> Predicate)[]
        {
            (
                Guid.Parse("00000002-0003-0000-0000-000000000002"),
                "admin-identidad",
                "Administrador de Identidad",
                "CRUD de usuarios, roles, permisos y asignaciones.",
                p => p.Codigo.StartsWith("identidad.", StringComparison.Ordinal)
            ),
            (
                Guid.Parse("00000002-0003-0000-0000-000000000003"),
                "admin-organizacional",
                "Administrador Organizacional",
                "CRUD de empresas, sucursales, departamentos, puestos y empleados + lectura de catálogos cross-empresa.",
                // D2: admin.sucursales.* (único permiso bajo el prefijo hoy:
                // admin.sucursales.departamentos-gestionar, el N:M). El punto
                // final acota a sub-recursos reales de "Admin Sucursales".
                // D3: compartido.catalogos.leer vive en el namespace
                // compartido.* (NO admin.*), así que NO lo captura el prefijo
                // admin.sucursales. — se agrega EXPLÍCITO (match exacto) para
                // que el org-admin pueda leer/usar el Sheet del N:M, coherente
                // con sus roles hermanos (admin-catálogos, admin-datos-maestros,
                // auditor) que ya lo tienen.
                p => p.Codigo.StartsWith("admin.empresas", StringComparison.Ordinal)
                     || p.Codigo.StartsWith("admin.departamentos", StringComparison.Ordinal)
                     || p.Codigo.StartsWith("admin.sucursales.", StringComparison.Ordinal)
                     // ADM-PR1: master de puestos y empleados (doc
                     // 10-catalogo-puestos-empleados) — catálogo
                     // organizacional, mismo dueño que sucursales/deptos.
                     || p.Codigo.StartsWith("admin.puestos.", StringComparison.Ordinal)
                     || p.Codigo.StartsWith("admin.empleados.", StringComparison.Ordinal)
                     || p.Codigo == PermisosCanonicos.CompartidoCatalogosLeer
            ),
            (
                Guid.Parse("00000002-0003-0000-0000-000000000004"),
                "admin-catalogos",
                "Administrador de Catálogos",
                "CRUD de catálogos cross-empresa (monedas, condiciones, SAT, etc.).",
                p => p.Codigo.StartsWith("compartido.catalogos", StringComparison.Ordinal)
            ),
            (
                Guid.Parse("00000002-0003-0000-0000-000000000005"),
                "admin-datos-maestros",
                "Administrador de Datos Maestros",
                "Gestión de proveedores y artículos (catálogos operativos).",
                p => p.Codigo.StartsWith("compartido.catalogos", StringComparison.Ordinal)
            ),
            (
                Guid.Parse("00000002-0003-0000-0000-000000000006"),
                "auditor",
                "Auditor",
                "Acceso read-only sobre todo el área Admin + bitácora.",
                p => p.Codigo == PermisosCanonicos.InfraAuditLogLeer
                     || p.Codigo == PermisosCanonicos.AdminAuditoriaLeer
                     || p.Codigo.EndsWith(".leer", StringComparison.Ordinal)
            ),
            (
                Guid.Parse("00000002-0003-0000-0000-000000000007"),
                "admin-compras",
                "Administrador Compras",
                "Configuración del módulo Compras de la empresa actual.",
                p => p.Codigo.StartsWith("compras.configuracion", StringComparison.Ordinal)
            ),
        };

        foreach (var def in rolesDefinicion)
        {
            var rol = await db.Roles
                .FirstOrDefaultAsync(r => r.Codigo == def.Codigo, cancellationToken);

            if (rol is null)
            {
                rol = new Rol(def.RolId, def.Codigo, def.Nombre, esDelSistema: true, def.Descripcion);
                db.Roles.Add(rol);
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Rol MVP '{Codigo}' creado con id={RolId}", def.Codigo, rol.Id);
            }

            var permisosEsperados = PermisosCanonicos.Todos
                .Where(def.Predicate)
                .Select(p => p.Id)
                .ToHashSet();

            var existingPermisoIds = await db.RolPermisos
                .Where(rp => rp.RolId == rol.Id)
                .Select(rp => rp.PermisoId)
                .ToListAsync(cancellationToken);

            var faltantes = permisosEsperados.Except(existingPermisoIds).ToList();
            if (faltantes.Count > 0)
            {
                foreach (var permisoId in faltantes)
                {
                    db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rol.Id, permisoId));
                }
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation(
                    "Asignados {Count} permisos faltantes al rol '{Codigo}' (total esperado: {Total}).",
                    faltantes.Count, def.Codigo, permisosEsperados.Count);
            }
        }
    }

    /// <summary>
    /// Crea el Usuario + UsuarioPreferencia si no existe. Si existe (re-arranque),
    /// devuelve el ya creado sin modificar.
    /// </summary>
    private async Task<Usuario> EnsureSuperAdminUsuarioAsync(
        IdentidadDbContext db,
        string oid,
        CancellationToken cancellationToken)
    {
        var usuario = await db.Usuarios
            .FirstOrDefaultAsync(u => u.EntraOid == oid, cancellationToken);

        if (usuario is not null)
        {
            // B.1: en re-arranques de dev/UAT, asegurar que el superadmin
            // tenga DepartamentoId asignado (si el catálogo lo permite).
            await EnsureSuperAdminDepartamentoAsync(db, usuario, cancellationToken);

            _logger.LogInformation(
                "Usuario SuperAdmin ya existe (oid={Oid}, userId={UserId}). Bootstrap idempotente.",
                oid, usuario.Id);
            return usuario;
        }

        // Datos placeholder: el primer login sobreescribe email y nombre con
        // datos reales del token de Entra (auto-provisión vía LoginOrchestrator
        // detecta el usuario por oid y lo usa directamente).
        var placeholderEmail = $"{oid}@bootstrap.local";
        var placeholderNombre = "SuperAdmin (configurar en primer login)";

        usuario = new Usuario(Guid.CreateVersion7(), oid, placeholderEmail, placeholderNombre);
        var preferencia = new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id);

        db.Usuarios.Add(usuario);
        db.UsuarioPreferencias.Add(preferencia);
        await db.SaveChangesAsync(cancellationToken);

        // Asignar DepartamentoId si el seed del catálogo lo populó.
        await EnsureSuperAdminDepartamentoAsync(db, usuario, cancellationToken);

        _logger.LogInformation(
            "Usuario SuperAdmin creado: oid={Oid}, userId={UserId}", oid, usuario.Id);

        return usuario;
    }

    /// <summary>
    /// Asigna <c>DepartamentoId = COMPRAS</c> al superadmin si el catálogo
    /// (B.1) ya tiene esa fila. Idempotente: si ya está asignado, no-op.
    /// En producción, antes de que el script de cutover (B.6) corra, el
    /// catálogo está vacío y este método no hace nada — el cutover lo
    /// asignará explícitamente después.
    /// </summary>
    private async Task EnsureSuperAdminDepartamentoAsync(
        IdentidadDbContext db,
        Usuario usuario,
        CancellationToken cancellationToken)
    {
        if (usuario.DepartamentoId is not null) return;

        // Id deterministic del depto COMPRAS (seed B.1).
        var deptoComprasId = Guid.Parse("00000005-0004-0000-0000-000000000001");

        // Cross-schema: usamos raw SQL para evitar agregar dependencia a
        // CompartidoDbContext en el bootstrap. Si la fila no existe, no
        // tocamos el usuario.
        var deptoExiste = await db.Database
            .SqlQueryRaw<int>(
                "SELECT 1 AS \"Value\" FROM compartido.departamentos WHERE id = {0} LIMIT 1",
                deptoComprasId)
            .AnyAsync(cancellationToken);

        if (!deptoExiste) return;

        usuario.AsignarDepartamento(deptoComprasId);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "SuperAdmin DepartamentoId asignado: {DeptoId} (COMPRAS).",
            deptoComprasId);
    }

    /// <summary>
    /// Si <c>Auth:Bootstrap:EmpresaInicial</c> está configurado, crea la
    /// empresa en compartido y la asignación SuperAdmin↔empresa↔rol en
    /// identidad. Idempotente.
    /// </summary>
    private async Task EnsureEmpresaAndAssignmentAsync(
        IdentidadDbContext identidad,
        CompartidoDbContext compartido,
        Usuario usuario,
        Rol rol,
        CancellationToken cancellationToken)
    {
        var inicial = _options.Value.Bootstrap?.EmpresaInicial;
        if (inicial is null || string.IsNullOrWhiteSpace(inicial.Rfc))
        {
            _logger.LogInformation(
                "Auth:Bootstrap:EmpresaInicial no configurada. SuperAdmin queda sin asignación a empresa. " +
                "Crear empresa y UsuarioEmpresaRol manualmente para uso end-to-end.");
            return;
        }

        // Empresa: idempotente por ID determinista. Compartido es el owner del schema.
        var empresa = await compartido.Empresas
            .FirstOrDefaultAsync(e => e.Id == EmpresaInicialId, cancellationToken);

        if (empresa is null)
        {
            // F1-ADM-01: Clave/domicilio/moneda son campos nuevos sin captura
            // en Auth:Bootstrap:EmpresaInicial todavía (Fase 2 los agregará al
            // schema de configuración si hace falta). Clave = RFC en mayúsculas
            // (business key ya única); domicilio con placeholders explícitos.
            empresa = new Empresa(
                EmpresaInicialId,
                clave: inicial.Rfc.ToUpperInvariant(),
                inicial.Rfc,
                inicial.RazonSocial,
                inicial.RegimenFiscal,
                calle: "Sin especificar",
                numeroExterior: "S/N",
                colonia: "Sin especificar",
                ciudad: "Sin especificar",
                municipio: "Sin especificar",
                estado: "Sin especificar",
                pais: "México",
                nombreComercial: inicial.NombreComercial);
            compartido.Empresas.Add(empresa);
            await compartido.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Empresa inicial creada: id={EmpresaId}, rfc={Rfc}, razonSocial={RazonSocial}",
                empresa.Id, empresa.Rfc, empresa.RazonSocial);
        }

        // Asignación SuperAdmin ↔ Empresa ↔ Rol. Idempotente por unique idx.
        var assignment = await identidad.UsuarioEmpresaRoles
            .FirstOrDefaultAsync(
                uer => uer.UsuarioId == usuario.Id
                       && uer.EmpresaId == empresa.Id
                       && uer.RolId == rol.Id,
                cancellationToken);

        if (assignment is null)
        {
            assignment = new UsuarioEmpresaRol(
                Guid.CreateVersion7(),
                usuario.Id,
                empresa.Id,
                rol.Id,
                asignadoPorUsuarioId: null); // null = bootstrap del sistema
            identidad.UsuarioEmpresaRoles.Add(assignment);
            await identidad.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "SuperAdmin asignado a empresa {Rfc} con rol super-admin (assignmentId={AssignmentId})",
                empresa.Rfc, assignment.Id);
        }
    }
}
