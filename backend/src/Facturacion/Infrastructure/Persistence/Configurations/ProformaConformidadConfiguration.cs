using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Proformas;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ProformaConformidad"/> (M-234). Tabla
/// <c>facturacion.proformas_conformidad</c>; una conformidad por versión de proforma.
/// </summary>
public sealed class ProformaConformidadConfiguration : IEntityTypeConfiguration<ProformaConformidad>
{
    public void Configure(EntityTypeBuilder<ProformaConformidad> builder)
    {
        builder.ToTable("proformas_conformidad");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ProformaPedidoId).IsRequired();
        builder.Property(e => e.Quien).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Fecha).HasColumnType("date").IsRequired();
        builder.Property(e => e.Medio).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Notas).HasMaxLength(500);
        builder.Property(e => e.RegistradaPorUsuarioId);
        builder.Property(e => e.RegistradaPorNombre).HasMaxLength(200);
        builder.Property(e => e.RegistradaEn).IsRequired();
        // M-340: conformidad invalidada porque el pedido cambió después de aprobar la versión.
        builder.Property(e => e.InvalidadaEn);
        builder.Property(e => e.InvalidadaMotivo).HasMaxLength(1000);
        builder.Ignore(e => e.Invalidada);

        builder.HasIndex(e => e.ProformaPedidoId)
            .IsUnique()
            .HasDatabaseName("ux_proformas_conformidad_proforma");
    }
}
