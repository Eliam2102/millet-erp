using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.Infrastructure.Persistence;
public sealed partial class ComercioExteriorDbContext
{
    public DbSet<ConceptoGasto> ConceptosGasto => Set<ConceptoGasto>();
    public DbSet<AnticipoAgente> AnticiposAgente => Set<AnticipoAgente>();
    private static void Json<T>(PropertyBuilder<T> property)
    {
        property.HasColumnType("jsonb").HasConversion(v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<T>(v, (JsonSerializerOptions?)null)!);
        property.Metadata.SetValueComparer(new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!));
    }
    private static void ConfigurarFinanzas(ModelBuilder b)
    {
        var e = b.Entity<Expediente>();
        e.Ignore(x => x.EstadoFinanciero);
        e.HasMany(x => x.Gastos).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Gastos).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.PartidasCosto).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.PartidasCosto).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.Distribuciones).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Distribuciones).UsePropertyAccessMode(PropertyAccessMode.Field);
        e.HasMany(x => x.Anticipos).WithOne().HasForeignKey(x => x.ExpedienteId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Anticipos).UsePropertyAccessMode(PropertyAccessMode.Field);
        var c = b.Entity<ConceptoGasto>(); c.ToTable("conceptos_gasto"); c.HasKey(x => x.Id); c.Property(x => x.Id).ValueGeneratedNever();
        c.Property(x => x.Codigo).HasMaxLength(30); c.Property(x => x.Nombre).HasMaxLength(200); c.Ignore(x => x.EsIvaImportacion);
        c.HasIndex(x => new { x.EmpresaId, x.Codigo }).IsUnique();
        var g = b.Entity<GastoExpediente>(); g.ToTable("gastos_expediente"); g.HasKey(x => x.Id); g.Property(x => x.Id).ValueGeneratedNever();
        g.HasIndex(x => new { x.EmpresaId, x.OrdenServicioId, x.ExpedienteId });
        g.Property(x => x.OrdenServicioFolio).HasMaxLength(40);
        g.Property(x => x.OrdenServicioMoneda).HasMaxLength(3);
        g.Ignore(x => x.AggregateRootId); g.Ignore(x => x.Diferencia);
        g.HasOne<ConceptoGasto>().WithMany().HasForeignKey(x => x.ConceptoId).OnDelete(DeleteBehavior.Restrict);
        Json(g.Property(x => x.ImportesDirectos));
        var p = b.Entity<PartidaCosto>(); p.ToTable("partidas_costo"); p.HasKey(x => x.Id); p.Property(x => x.Id).ValueGeneratedNever(); p.Ignore(x => x.AggregateRootId);
        var d = b.Entity<DistribucionGasto>(); d.ToTable("distribuciones_gasto"); d.HasKey(x => x.Id); d.Property(x => x.Id).ValueGeneratedNever(); d.Ignore(x => x.AggregateRootId);
        d.HasIndex(x => new { x.ExpedienteId, x.NumeroVersion }).IsUnique(); Json(d.Property(x => x.Renglones)); Json(d.Property(x => x.Partidas));
        var a = b.Entity<AnticipoAgente>(); a.ToTable("anticipos_agente"); a.HasKey(x => x.Id); a.Property(x => x.Id).ValueGeneratedNever(); a.Ignore(x => x.AggregateRootId);
        a.Ignore(x => x.Anticipado); a.Ignore(x => x.Aplicado); a.Ignore(x => x.Diferencia); a.Ignore(x => x.Saldo); a.Ignore(x => x.Estado);
        a.HasIndex(x => new { x.EmpresaId, x.AgenteId }); Json(a.Property(x => x.Resoluciones));
        foreach (var tipo in new[] { typeof(GastoExpediente), typeof(PartidaCosto), typeof(AnticipoAgente) })
            foreach (var property in b.Entity(tipo).Metadata.GetProperties().Where(x => x.ClrType == typeof(decimal) || x.ClrType == typeof(decimal?)))
                b.Entity(tipo).Property(property.Name).HasPrecision(20, property.Name is "TipoCambio" or "OrdenServicioTipoCambio" or "PesoKg" ? 6 : 2);
    }
}
