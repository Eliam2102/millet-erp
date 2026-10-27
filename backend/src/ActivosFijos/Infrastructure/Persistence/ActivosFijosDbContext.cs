using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.ActivosFijos.Infrastructure.Persistence;
public sealed partial class ActivosFijosDbContext(DbContextOptions<ActivosFijosDbContext> options, ICurrentEmpresaContext empresa) : BaseDbContext(options, empresa)
{
    public const string SchemaName = "activos_fijos";
    public DbSet<FacturaActivo> Facturas => Set<FacturaActivo>();
    public DbSet<ClaseActivo> Clases => Set<ClaseActivo>();
    public DbSet<Activo> Activos => Set<Activo>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        var secuencia = modelBuilder.Entity<SecuenciaActivo>();
        secuencia.ToTable("secuencias"); secuencia.HasKey(x => new { x.EmpresaId, x.ClaseId });
        var f = modelBuilder.Entity<FacturaActivo>();
        f.ToTable("facturas_conciliadas"); f.HasKey(x => x.Id); f.Property(x => x.Id).ValueGeneratedNever();
        f.HasIndex(x => new { x.EmpresaId, x.LineaFacturaId }).IsUnique();
        var c = modelBuilder.Entity<ClaseActivo>();
        c.ToTable("clases"); c.HasKey(x => x.Id); c.Property(x => x.Id).ValueGeneratedNever();
        c.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique();
        c.Property(x => x.Clave).HasMaxLength(20); c.Property(x => x.Nombre).HasMaxLength(200);
        c.Property(x => x.TasaFiscalAnual).HasPrecision(5, 2);
        var fechaSemilla = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var clase in CatalogoClasesMillet.Clases)
            c.HasData(new { Id = Guid.Parse($"0000000f-0007-0000-0000-{int.Parse(clase.Clave):000000000000}"), EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001"),
                clase.Clave, clase.Nombre, TasaFiscalAnual = clase.Tasa, TasaContableAnual = clase.TasaContable, clase.Depreciable, clase.TasaConfirmada, RequiereRevision = false,
                Metodo = "LineaRecta", Activa = true, Version = 1, CreatedAt = fechaSemilla, UpdatedAt = fechaSemilla });
        var inpc = modelBuilder.Entity<InpcMensual>();
        inpc.ToTable("inpc_mensual", t => t.HasCheckConstraint("ck_inpc_valido", "anio BETWEEN 1 AND 9998 AND mes BETWEEN 1 AND 12 AND valor > 0"));
        inpc.HasKey(x => x.Id); inpc.Property(x => x.Id).ValueGeneratedNever();
        inpc.HasIndex(x => new { x.EmpresaId, x.Anio, x.Mes }).IsUnique();
        var a = modelBuilder.Entity<Activo>();
        a.ToTable("activos"); a.HasKey(x => x.Id); a.Property(x => x.Id).ValueGeneratedNever();
        a.HasIndex(x => new { x.EmpresaId, x.Numero }).IsUnique();
        a.HasIndex(x => new { x.EmpresaId, x.RecepcionId, x.LineaOcId, x.UnidadRecepcion }).IsUnique();
        a.HasIndex(x => new { x.EmpresaId, x.SucursalId, x.Estado });
        a.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoMejoradoId).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ClaseActivo>().WithMany().HasForeignKey(x => x.ClaseId).OnDelete(DeleteBehavior.Restrict);
        a.Property(x => x.Numero).HasMaxLength(60); a.Property(x => x.Descripcion).HasMaxLength(500); a.Property(x => x.MotivoBaja).HasMaxLength(500);
        a.Ignore(x => x.ValorEnLibros); a.Ignore(x => x.EnTraslado); a.Ignore(x => x.PuedeAceptarTraslado);
        a.Property(x => x.Estado).HasConversion<string>(); a.Property(x => x.Importacion).HasConversion<string>();
        a.HasMany(x => x.Historial).WithOne().HasForeignKey(x => x.ActivoId).OnDelete(DeleteBehavior.Restrict);
        a.HasMany(x => x.Traslados).WithOne().HasForeignKey(x => x.ActivoId).OnDelete(DeleteBehavior.Restrict);
        var h = modelBuilder.Entity<MovimientoValorActivo>();
        h.ToTable("movimientos_valor"); h.HasKey(x => x.Id); h.Property(x => x.Id).ValueGeneratedNever();
        h.HasIndex(x => new { x.ActivoId, x.DocumentoId }).IsUnique();
        var t = modelBuilder.Entity<TrasladoActivo>();
        t.ToTable("traslados"); t.HasKey(x => x.Id); t.Property(x => x.Id).ValueGeneratedNever();
        t.OwnsOne(x => x.Origen); t.OwnsOne(x => x.Destino);
        t.HasIndex(x => x.ActivoId).IsUnique().HasFilter("aceptado_en IS NULL");
        var corrida = modelBuilder.Entity<CorridaDepreciacion>();
        corrida.ToTable("corridas"); corrida.HasKey(x => x.Id); corrida.Property(x => x.Id).ValueGeneratedNever();
        corrida.HasIndex(x => new { x.EmpresaId, x.Periodo, x.Revision }).IsUnique();
        corrida.HasIndex(x => new { x.EmpresaId, x.Periodo }).IsUnique().HasFilter("NOT anulada");
        corrida.HasMany(x => x.Detalles).WithOne().HasForeignKey(x => x.CorridaId).OnDelete(DeleteBehavior.Restrict);
        var detalle = modelBuilder.Entity<DetalleDepreciacion>();
        detalle.ToTable("depreciaciones"); detalle.HasKey(x => x.Id); detalle.Property(x => x.Id).ValueGeneratedNever();
        detalle.Ignore(x => x.AcumuladaDespues); detalle.HasIndex(x => new { x.CorridaId, x.ActivoId }).IsUnique();
        var operacion = modelBuilder.Entity<OperacionLibro>();
        operacion.ToTable("operaciones_libro"); operacion.HasKey(x => x.Id); operacion.Property(x => x.Id).ValueGeneratedNever();
        operacion.Property(x => x.SolicitadoPor).HasMaxLength(200).IsRequired();
        operacion.HasIndex(x => new { x.EmpresaId, x.FacturaId }).IsUnique();
        operacion.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId).OnDelete(DeleteBehavior.Restrict);
        var configuracion = modelBuilder.Entity<ConfiguracionLibro>();
        configuracion.ToTable("configuracion_libro"); configuracion.HasKey(x => x.Id); configuracion.Property(x => x.Id).ValueGeneratedNever();
        configuracion.HasIndex(x => x.EmpresaId).IsUnique();
        var carga = modelBuilder.Entity<CargaLibro>();
        carga.ToTable("cargas_libro"); carga.HasKey(x => x.Id); carga.Property(x => x.Id).ValueGeneratedNever();
        carga.HasIndex(x => new { x.EmpresaId, x.Huella, x.VersionArchivo }).IsUnique();
        ConfigurarConservacion(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties().Where(x => (Nullable.GetUnderlyingType(x.ClrType) ?? x.ClrType) == typeof(decimal))) { p.SetPrecision(18); p.SetScale(p.Name == "TipoCambio" ? 6 : 2); }
        c.Property(x => x.TasaContableAnual).HasPrecision(18, 4);
        c.Property(x => x.TasaFiscalAnual).HasPrecision(18, 4);
        inpc.Property(x => x.Valor).HasPrecision(18, 6);
        base.OnModelCreating(modelBuilder);
    }
}
