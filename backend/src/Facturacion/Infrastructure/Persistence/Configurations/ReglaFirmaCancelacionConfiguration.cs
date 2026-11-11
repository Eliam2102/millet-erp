using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Cancelaciones;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// M-235: tabla <c>facturacion.regla_firma_cancelacion</c>. Nace vacía (una firma para todo);
/// la configura el Contador General.
/// </summary>
public sealed class ReglaFirmaCancelacionConfiguration : IEntityTypeConfiguration<ReglaFirmaCancelacion>
{
    public void Configure(EntityTypeBuilder<ReglaFirmaCancelacion> builder)
    {
        builder.ToTable("regla_firma_cancelacion", t =>
        {
            t.HasCheckConstraint("ck_regla_firma_cancelacion_firmas", "firmas_requeridas IN (1, 2)");
            t.HasCheckConstraint("ck_regla_firma_cancelacion_umbral", "umbral_mxn >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.TipoComprobante).HasMaxLength(40);
        builder.Property(e => e.UmbralMxn).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.FirmasRequeridas).IsRequired();

        builder.HasIndex(e => new { e.EmpresaId, e.TipoComprobante, e.UmbralMxn })
            .IsUnique()
            .HasDatabaseName("ux_regla_firma_cancelacion");
    }
}
