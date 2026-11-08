using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.ConciliacionSat;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

public sealed class ConciliacionSatConfiguration : IEntityTypeConfiguration<ConciliacionSat>
{
    public void Configure(EntityTypeBuilder<ConciliacionSat> builder)
    {
        builder.ToTable("conciliacion_sat", t =>
        {
            t.HasCheckConstraint("ck_conciliacion_sat_fuente", "fuente IN (1, 2)");
            t.HasCheckConstraint("ck_conciliacion_sat_periodo", "hasta >= desde");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Fuente).HasConversion<short>().IsRequired();
        builder.Property(e => e.NombreArchivo).HasMaxLength(260).IsRequired();
        builder.Property(e => e.Huella).HasMaxLength(64).IsRequired();
        builder.Ignore(e => e.SinDiferencias);

        builder.HasMany(e => e.Diferencias).WithOne().HasForeignKey(d => d.ConciliacionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Diferencias).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.EmpresaId, e.EjecutadaEn }).HasDatabaseName("ix_conciliacion_sat_empresa_fecha");
    }
}

public sealed class DiferenciaConciliacionSatConfiguration : IEntityTypeConfiguration<DiferenciaConciliacionSat>
{
    public void Configure(EntityTypeBuilder<DiferenciaConciliacionSat> builder)
    {
        builder.ToTable("conciliacion_sat_diferencia", t =>
            t.HasCheckConstraint("ck_conciliacion_sat_diferencia_tipo", "tipo BETWEEN 1 AND 6"));
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Uuid).HasMaxLength(36).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Folio).HasMaxLength(40);
        builder.Property(e => e.RfcReceptor).HasMaxLength(13);
        builder.Property(e => e.ValorSat).HasMaxLength(120);
        builder.Property(e => e.ValorErp).HasMaxLength(120);
        builder.Property(e => e.Nota).HasMaxLength(DiferenciaConciliacionSat.MaximoNota);

        builder.HasIndex(e => new { e.ConciliacionId, e.Tipo }).HasDatabaseName("ix_conciliacion_sat_diferencia_corrida_tipo");
        builder.HasIndex(e => new { e.EmpresaId, e.Uuid }).HasDatabaseName("ix_conciliacion_sat_diferencia_empresa_uuid");
    }
}
