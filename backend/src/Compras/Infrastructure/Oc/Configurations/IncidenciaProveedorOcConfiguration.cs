using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

public sealed class IncidenciaProveedorOcConfiguration : IEntityTypeConfiguration<IncidenciaProveedorOc>
{
    public void Configure(EntityTypeBuilder<IncidenciaProveedorOc> builder)
    {
        builder.ToTable("oc_incidencias_proveedor", t =>
        {
            t.HasCheckConstraint("ck_oc_incidencias_proveedor_tipo", "tipo IN (1, 2, 3)");
            t.HasCheckConstraint("ck_oc_incidencias_proveedor_cantidad", "cantidad > 0");
        });
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Ignore(i => i.AggregateRootId);
        builder.Ignore(i => i.Abierta);
        builder.Ignore(i => i.FechaCompromisoVigente);
        builder.Property(i => i.Tipo).HasConversion<short>();
        builder.Property(i => i.Cantidad).HasColumnType("numeric(15,4)");
        builder.Property(i => i.Descripcion).HasMaxLength(500);
        builder.Property(i => i.MotivoCierre).HasMaxLength(300);
        builder.HasIndex(i => new { i.OrdenCompraId, i.Numero }).IsUnique()
            .HasDatabaseName("ux_oc_incidencias_proveedor_numero");
        builder.HasIndex(i => i.LineaOrdenCompraId);
        builder.HasOne<LineaOrdenCompra>().WithMany()
            .HasForeignKey(i => i.LineaOrdenCompraId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Compromisos).WithOne()
            .HasForeignKey(c => c.IncidenciaProveedorOcId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Compromisos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class CompromisoIncidenciaOcConfiguration : IEntityTypeConfiguration<CompromisoIncidenciaOc>
{
    public void Configure(EntityTypeBuilder<CompromisoIncidenciaOc> builder)
    {
        builder.ToTable("oc_incidencias_compromisos");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Ignore(c => c.AggregateRootId);
        builder.Property(c => c.Motivo).HasMaxLength(300);
        builder.HasIndex(c => new { c.IncidenciaProveedorOcId, c.Numero }).IsUnique()
            .HasDatabaseName("ux_oc_incidencias_compromisos_numero");
    }
}

public sealed class AvisoIncidenciaProveedorOcConfiguration : IEntityTypeConfiguration<AvisoIncidenciaProveedorOc>
{
    public void Configure(EntityTypeBuilder<AvisoIncidenciaProveedorOc> builder)
    {
        builder.ToTable("oc_incidencias_avisos");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.HasOne<IncidenciaProveedorOc>().WithMany()
            .HasForeignKey(a => a.IncidenciaProveedorOcId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.IncidenciaProveedorOcId, a.FechaCompromiso }).IsUnique()
            .HasDatabaseName("ux_oc_incidencias_avisos_compromiso");
    }
}
