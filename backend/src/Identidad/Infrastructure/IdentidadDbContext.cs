using Microsoft.EntityFrameworkCore;
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
/// DbContext del módulo Identidad. Gestiona usuarios, roles, permisos y
/// asignaciones (incluyendo SoD vía <c>RestriccionRol</c>). Schema:
/// <c>identidad</c>. Cross-schema FK hacia <c>compartido.empresas</c> para
/// el multi-tenancy de <see cref="UsuarioEmpresaRol"/>.
/// Ver ADR-0005, ADR-0007 y ADR-0011.
/// </summary>
public sealed class IdentidadDbContext : BaseDbContext
{
    public IdentidadDbContext(
        DbContextOptions<IdentidadDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Permiso> Permisos => Set<Permiso>();
    public DbSet<RolPermiso> RolPermisos => Set<RolPermiso>();
    public DbSet<RolGrupoEntraId> RolGruposEntraId => Set<RolGrupoEntraId>();
    public DbSet<UsuarioEmpresaRol> UsuarioEmpresaRoles => Set<UsuarioEmpresaRol>();
    public DbSet<RestriccionRol> RestriccionesRol => Set<RestriccionRol>();
    public DbSet<UsuarioPreferencia> UsuarioPreferencias => Set<UsuarioPreferencia>();
    public DbSet<UsuarioServicio> UsuariosServicio => Set<UsuarioServicio>();
    public DbSet<UsuarioServicioPermiso> UsuarioServicioPermisos => Set<UsuarioServicioPermiso>();
    // F1-ADM-01 Fase 1: scoping de usuarios por sucursal. UNIQUE
    // (UsuarioId, SucursalId). FK física cross-schema a
    // compartido.sucursales, mismo patrón que UsuarioServicio → Empresa.
    public DbSet<UsuarioSucursal> UsuarioSucursales => Set<UsuarioSucursal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("identidad");
        base.OnModelCreating(modelBuilder);

        ConfigureEmpresaReference(modelBuilder);
        ConfigureSucursalReference(modelBuilder);
        ConfigureUsuario(modelBuilder);
        ConfigureRol(modelBuilder);
        ConfigurePermiso(modelBuilder);
        ConfigureRolPermiso(modelBuilder);
        ConfigureRolGrupoEntraId(modelBuilder);
        ConfigureUsuarioEmpresaRol(modelBuilder);
        ConfigureRestriccionRol(modelBuilder);
        ConfigureUsuarioPreferencia(modelBuilder);
        ConfigureUsuarioServicio(modelBuilder);
        ConfigureUsuarioServicioPermiso(modelBuilder);
        ConfigureUsuarioSucursal(modelBuilder);

        SeedPermisosCanonicos(modelBuilder);
    }

    /// <summary>
    /// Mapea <see cref="Empresa"/> a <c>compartido.empresas</c> sin gestionar
    /// su migración (CompartidoDbContext la owna). Necesario para que las FK
    /// cross-schema desde <c>UsuarioEmpresaRol</c> y <c>UsuarioPreferencia</c>
    /// resuelvan correctamente. Mismo patrón que <c>audit_log</c> en
    /// <c>BaseDbContext</c>.
    /// </summary>
    private static void ConfigureEmpresaReference(ModelBuilder modelBuilder)
    {
        var empresa = modelBuilder.Entity<Empresa>();
        empresa.ToTable("empresas", "compartido");
        empresa.HasKey(x => x.Id);
        empresa.Property(x => x.Rfc).HasMaxLength(13).IsRequired();
        empresa.Property(x => x.RazonSocial).HasMaxLength(254).IsRequired();
        empresa.Property(x => x.NombreComercial).HasMaxLength(254);
        empresa.Property(x => x.RegimenFiscal).HasMaxLength(10).IsRequired();
        empresa.ToTable(t => t.ExcludeFromMigrations());
    }

    /// <summary>
    /// Mapea <see cref="Sucursal"/> a <c>compartido.sucursales</c> sin
    /// gestionar su migración (CompartidoDbContext la owna). Necesario
    /// para que la FK cross-schema desde <see cref="UsuarioSucursal"/>
    /// resuelva correctamente. Mismo patrón que
    /// <see cref="ConfigureEmpresaReference"/>.
    /// </summary>
    private static void ConfigureSucursalReference(ModelBuilder modelBuilder)
    {
        var sucursal = modelBuilder.Entity<Sucursal>();
        sucursal.ToTable("sucursales", "compartido");
        sucursal.HasKey(x => x.Id);
        sucursal.Property(x => x.Clave).HasMaxLength(20).IsRequired();
        sucursal.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        sucursal.ToTable(t => t.ExcludeFromMigrations());
    }

    private static void ConfigureUsuario(ModelBuilder modelBuilder)
    {
        var usuario = modelBuilder.Entity<Usuario>();
        usuario.ToTable("usuarios");
        usuario.HasKey(x => x.Id);
        usuario.HasIndex(x => x.EntraOid).IsUnique();
        usuario.HasIndex(x => x.Email).IsUnique();
        usuario.Property(x => x.EntraOid).HasMaxLength(100).IsRequired();
        usuario.Property(x => x.Email).HasMaxLength(254).IsRequired();
        usuario.Property(x => x.Nombre).HasMaxLength(254).IsRequired();
        // B.1: DepartamentoId nullable. FK lógica a compartido.departamentos
        // (cross-schema → sin FK física por convención §10.2.3 del diseño).
        usuario.Property(x => x.DepartamentoId);
        usuario.HasIndex(x => x.DepartamentoId);
        // Alta unificada (F1-ADM-01 plan 15): ciclo de vida del acceso
        // respecto de Entra ID y marca de cuenta técnica (D4).
        usuario.Property(x => x.EstadoAcceso).HasConversion<short>().IsRequired();
        usuario.Property(x => x.PrimerAccesoEn);
        usuario.Property(x => x.MotivoErrorProvision)
            .HasMaxLength(Usuario.MotivoErrorProvisionMaxLength);
        usuario.Property(x => x.EsCuentaTecnica).IsRequired();
        usuario.HasIndex(x => x.EstadoAcceso);
    }

    private static void ConfigureRol(ModelBuilder modelBuilder)
    {
        var rol = modelBuilder.Entity<Rol>();
        rol.ToTable("roles");
        rol.HasKey(x => x.Id);
        rol.HasIndex(x => x.Codigo).IsUnique();
        rol.Property(x => x.Codigo).HasMaxLength(64).IsRequired();
        rol.Property(x => x.Nombre).HasMaxLength(100).IsRequired();
        rol.Property(x => x.Descripcion).HasMaxLength(500);
    }

    private static void ConfigurePermiso(ModelBuilder modelBuilder)
    {
        var permiso = modelBuilder.Entity<Permiso>();
        permiso.ToTable("permisos");
        permiso.HasKey(x => x.Id);
        permiso.HasIndex(x => x.Codigo).IsUnique();
        permiso.HasIndex(x => x.Modulo); // queries "todos los permisos del módulo X"
        permiso.Property(x => x.Codigo).HasMaxLength(200).IsRequired();
        permiso.Property(x => x.Modulo).HasMaxLength(50).IsRequired();
        permiso.Property(x => x.Recurso).HasMaxLength(100).IsRequired();
        permiso.Property(x => x.Accion).HasMaxLength(50).IsRequired();
        permiso.Property(x => x.Descripcion).HasMaxLength(500).IsRequired();
    }

    private static void ConfigureRolPermiso(ModelBuilder modelBuilder)
    {
        var rolPermiso = modelBuilder.Entity<RolPermiso>();
        rolPermiso.ToTable("rol_permisos");
        rolPermiso.HasKey(x => x.Id);
        rolPermiso.HasIndex(x => new { x.RolId, x.PermisoId }).IsUnique();

        rolPermiso.HasOne<Rol>()
            .WithMany()
            .HasForeignKey(x => x.RolId)
            .OnDelete(DeleteBehavior.Cascade); // borrar rol borra sus permisos asignados

        rolPermiso.HasOne<Permiso>()
            .WithMany()
            .HasForeignKey(x => x.PermisoId)
            .OnDelete(DeleteBehavior.Restrict); // no borrar permiso si está asignado
    }

    private static void ConfigureRolGrupoEntraId(ModelBuilder modelBuilder)
    {
        var rge = modelBuilder.Entity<RolGrupoEntraId>();
        rge.ToTable("rol_grupos_entra_id");
        rge.HasKey(x => x.Id);
        rge.HasIndex(x => new { x.RolId, x.ObjectId }).IsUnique();
        rge.Property(x => x.ObjectId).HasMaxLength(100).IsRequired();
        rge.Property(x => x.Nombre).HasMaxLength(254).IsRequired();

        rge.HasOne<Rol>()
            .WithMany()
            .HasForeignKey(x => x.RolId)
            .OnDelete(DeleteBehavior.Cascade); // borrar rol borra sus asociaciones a grupos Entra ID
    }

    private static void ConfigureUsuarioEmpresaRol(ModelBuilder modelBuilder)
    {
        var ueRol = modelBuilder.Entity<UsuarioEmpresaRol>();
        ueRol.ToTable("usuario_empresa_roles");
        ueRol.HasKey(x => x.Id);
        ueRol.HasIndex(x => new { x.UsuarioId, x.EmpresaId, x.RolId }).IsUnique();
        ueRol.HasIndex(x => new { x.EmpresaId, x.UsuarioId }); // listar usuarios de la empresa

        ueRol.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade); // borrar usuario borra sus asignaciones

        ueRol.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict); // no borrar empresa si tiene usuarios

        ueRol.HasOne<Rol>()
            .WithMany()
            .HasForeignKey(x => x.RolId)
            .OnDelete(DeleteBehavior.Restrict); // no borrar rol si está asignado a alguien

        // FK opcional al usuario que hizo la asignación. Restrict para evitar
        // cycle de cascade con la FK principal a UsuarioId.
        ueRol.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(x => x.AsignadoPorUsuarioId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }

    private static void ConfigureRestriccionRol(ModelBuilder modelBuilder)
    {
        var restriccion = modelBuilder.Entity<RestriccionRol>();
        restriccion.ToTable("restricciones_rol");
        restriccion.HasKey(x => x.Id);
        restriccion.HasIndex(x => new { x.RolAId, x.RolBId }).IsUnique();
        restriccion.Property(x => x.Razon).HasMaxLength(500).IsRequired();

        restriccion.HasOne<Rol>()
            .WithMany()
            .HasForeignKey(x => x.RolAId)
            .OnDelete(DeleteBehavior.Cascade);

        restriccion.HasOne<Rol>()
            .WithMany()
            .HasForeignKey(x => x.RolBId)
            .OnDelete(DeleteBehavior.Restrict); // evita cycle de cascade con RolAId
    }

    private static void ConfigureUsuarioPreferencia(ModelBuilder modelBuilder)
    {
        var pref = modelBuilder.Entity<UsuarioPreferencia>();
        pref.ToTable("usuario_preferencias");
        pref.HasKey(x => x.Id);
        pref.HasIndex(x => x.UsuarioId).IsUnique(); // 1:1 con Usuario
        pref.Property(x => x.IdiomaUI).HasMaxLength(10).IsRequired();
        pref.Property(x => x.ZonaHoraria).HasMaxLength(50);
        pref.Property(x => x.TemaUI).HasMaxLength(20).IsRequired();

        pref.HasOne<Usuario>()
            .WithOne()
            .HasForeignKey<UsuarioPreferencia>(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);

        pref.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.UltimaEmpresaId)
            .OnDelete(DeleteBehavior.SetNull) // si borran la empresa, limpia el recordatorio
            .IsRequired(false);
    }

    /// <summary>
    /// Configura <see cref="UsuarioServicio"/> (PR A — service principals).
    /// FK física cross-schema a <c>compartido.empresas</c> (Postgres soporta
    /// cross-schema FKs); índice único en <c>entra_appid</c> garantiza el
    /// match 1:1 con la App Registration de Entra. Índice normal en
    /// <c>entra_object_id</c> para diagnóstico (no es PK lógica).
    /// </summary>
    private static void ConfigureUsuarioServicio(ModelBuilder modelBuilder)
    {
        var sp = modelBuilder.Entity<UsuarioServicio>();
        sp.ToTable("usuario_servicio");
        sp.HasKey(x => x.Id);
        sp.HasIndex(x => x.EntraAppId).IsUnique();
        sp.HasIndex(x => x.EntraObjectId);
        sp.HasIndex(x => x.EmpresaId); // listado por empresa para admin futuro
        sp.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        sp.Property(x => x.EntraAppId).IsRequired();
        sp.Property(x => x.EntraObjectId).IsRequired();
        sp.Property(x => x.EmpresaId).IsRequired();
        sp.Property(x => x.Activo).HasDefaultValue(true).IsRequired();
        sp.Property(x => x.CreatedAtUtc).IsRequired();
        sp.Property(x => x.DeactivatedAtUtc);
        sp.Property(x => x.Notes).HasColumnType("text");

        sp.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Configura <see cref="UsuarioServicioPermiso"/>. PK compuesta
    /// <c>(UsuarioServicioId, PermisoClave)</c>; FK a <c>UsuarioServicio</c>
    /// con cascade (al borrar el SP se borran sus permisos asignados).
    /// La columna <c>permiso_clave</c> es el código string del permiso, NO
    /// un FK al id de <c>identidad.permiso</c> — ver doc del entity.
    /// </summary>
    private static void ConfigureUsuarioServicioPermiso(ModelBuilder modelBuilder)
    {
        var perm = modelBuilder.Entity<UsuarioServicioPermiso>();
        perm.ToTable("usuario_servicio_permiso");
        perm.HasKey(x => new { x.UsuarioServicioId, x.PermisoClave });
        perm.Property(x => x.PermisoClave).HasMaxLength(120).IsRequired();

        perm.HasOne<UsuarioServicio>()
            .WithMany()
            .HasForeignKey(x => x.UsuarioServicioId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// Configura <see cref="UsuarioSucursal"/> (F1-ADM-01 Fase 1 —
    /// scoping de usuarios por sucursal). FK física cross-schema a
    /// <c>compartido.sucursales</c> y a <c>compartido.empresas</c>, mismo
    /// mecanismo que <see cref="ConfigureUsuarioServicio"/>. Índice único
    /// en <c>(UsuarioId, SucursalId)</c>.
    /// </summary>
    private static void ConfigureUsuarioSucursal(ModelBuilder modelBuilder)
    {
        var us = modelBuilder.Entity<UsuarioSucursal>();
        us.ToTable("usuario_sucursales");
        us.HasKey(x => x.Id);
        us.Property(x => x.UsuarioId).IsRequired();
        us.Property(x => x.SucursalId).IsRequired();
        us.Property(x => x.EmpresaId).IsRequired();
        us.Property(x => x.Estatus).HasConversion<short>().IsRequired();

        us.HasIndex(x => new { x.UsuarioId, x.SucursalId }).IsUnique();
        us.HasIndex(x => x.SucursalId);
        us.HasIndex(x => x.EmpresaId);

        us.HasOne<Usuario>()
            .WithMany()
            .HasForeignKey(x => x.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade); // borrar usuario borra sus asignaciones de sucursal

        us.HasOne<Sucursal>()
            .WithMany()
            .HasForeignKey(x => x.SucursalId)
            .OnDelete(DeleteBehavior.Restrict);

        us.HasOne<Empresa>()
            .WithMany()
            .HasForeignKey(x => x.EmpresaId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    /// <summary>
    /// Inserta el catálogo canónico de permisos en la migration. GUIDs
    /// deterministas para idempotencia. Cada nuevo permiso que se agregue
    /// a <see cref="PermisosCanonicos.Todos"/> requerirá una nueva migration
    /// (EF detecta el delta del seed).
    /// </summary>
    private static void SeedPermisosCanonicos(ModelBuilder modelBuilder)
    {
        var seedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var seedRows = PermisosCanonicos.Todos.Select(p =>
        {
            var partes = p.Codigo.Split('.');
            return new
            {
                Id = p.Id,
                Codigo = p.Codigo,
                Modulo = partes[0],
                Recurso = partes[1],
                Accion = partes[2],
                Descripcion = p.Descripcion,
                Version = 1,
                CreatedAt = seedTime,
                UpdatedAt = seedTime,
                CreatedBy = (string?)"seed",
                UpdatedBy = (string?)"seed",
                DeletedAt = (DateTimeOffset?)null,
            };
        }).ToArray();

        modelBuilder.Entity<Permiso>().HasData(seedRows);
    }
}
