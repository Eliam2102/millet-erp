using Microsoft.EntityFrameworkCore;
using Millet.ActivosFijos.Domain;
namespace Millet.ActivosFijos.Infrastructure.Persistence;
public sealed partial class ActivosFijosDbContext
{
    public DbSet<DocumentoActivo> Documentos => Set<DocumentoActivo>();
    public DbSet<MantenimientoActivo> Mantenimientos => Set<MantenimientoActivo>();
    public DbSet<ConteoActivo> Conteos => Set<ConteoActivo>();
    public DbSet<AvisoActivo> Avisos => Set<AvisoActivo>();
    private static void ConfigurarConservacion(ModelBuilder m)
    {
        var d = m.Entity<DocumentoActivo>();
        d.ToTable("documentos"); d.HasKey(x => x.Id); d.Property(x => x.Id).ValueGeneratedNever();
        d.Ignore(x => x.AggregateRootId); d.Property(x => x.Tipo).HasConversion<string>();
        d.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId).OnDelete(DeleteBehavior.Restrict);
        d.HasIndex(x => x.AdjuntoId).IsUnique(); d.HasIndex(x => x.ReemplazaA).IsUnique();
        d.Property(x => x.Folio).HasMaxLength(100); d.Property(x => x.Emisor).HasMaxLength(200); d.Property(x => x.Notas).HasMaxLength(2000);
        var h = m.Entity<MantenimientoActivo>();
        h.ToTable("mantenimientos"); h.HasKey(x => x.Id); h.Property(x => x.Id).ValueGeneratedNever(); h.Ignore(x => x.AggregateRootId);
        h.Property(x => x.Tipo).HasConversion<string>(); h.Property(x => x.Descripcion).HasMaxLength(2000);
        h.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId).OnDelete(DeleteBehavior.Restrict);
        var c = m.Entity<ConteoActivo>();
        c.ToTable("conteos"); c.HasKey(x => x.Id); c.Property(x => x.Id).ValueGeneratedNever(); c.Property(x => x.Ubicacion).HasMaxLength(500);
        c.HasIndex(x => new { x.EmpresaId, x.SucursalId });
        c.HasMany(x => x.Partidas).WithOne().HasForeignKey(x => x.ConteoId).OnDelete(DeleteBehavior.Restrict);
        var p = m.Entity<PartidaConteoActivo>();
        p.ToTable("partidas_conteo"); p.HasKey(x => x.Id); p.Property(x => x.Id).ValueGeneratedNever(); p.Ignore(x => x.AggregateRootId);
        p.HasIndex(x => new { x.ConteoId, x.Numero }).IsUnique(); p.Property(x => x.Resultado).HasConversion<string>();
        p.Property(x => x.Numero).HasMaxLength(60); p.Property(x => x.Evidencia).HasMaxLength(2000); p.Property(x => x.EvidenciaAutorizacion).HasMaxLength(2000);
        var a = m.Entity<AvisoActivo>();
        a.ToTable("avisos"); a.HasKey(x => x.Id); a.Property(x => x.Id).ValueGeneratedNever(); a.Property(x => x.Tipo).HasConversion<string>();
        a.HasIndex(x => new { x.EmpresaId, x.FuenteId, x.Tipo }).IsUnique();
        a.HasOne<Activo>().WithMany().HasForeignKey(x => x.ActivoId).OnDelete(DeleteBehavior.Restrict);
    }
}
