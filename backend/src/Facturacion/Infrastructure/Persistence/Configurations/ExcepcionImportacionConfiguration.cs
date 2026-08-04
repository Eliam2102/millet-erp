using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Ingesta;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ExcepcionImportacion"/> (F3-PR1). Tabla
/// <c>facturacion.bandeja_excepcion_importacion</c>. Índice por
/// <c>resuelto</c> para la bandeja de pendientes.
/// </summary>
public sealed class ExcepcionImportacionConfiguration : IEntityTypeConfiguration<ExcepcionImportacion>
{
    public void Configure(EntityTypeBuilder<ExcepcionImportacion> builder)
    {
        builder.ToTable("bandeja_excepcion_importacion");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.PedidoRef).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Motivo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Detalle).HasMaxLength(1000);
        builder.Property(e => e.Resuelto).IsRequired();
        builder.Property(e => e.ResueltoPor);
        builder.Property(e => e.ResueltoAt);

        builder.HasIndex(e => new { e.EmpresaId, e.Resuelto })
            .HasDatabaseName("ix_bandeja_excepcion_resuelto");
    }
}
