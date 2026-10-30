using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class ConciliacionConfiguration : IEntityTypeConfiguration<Conciliacion>
{
    public void Configure(EntityTypeBuilder<Conciliacion> builder)
    {
        builder.ToTable("conciliacion", t =>
        {
            t.HasCheckConstraint("ck_conciliacion_estado", "estado IN (1, 2)");
            t.HasCheckConstraint("ck_conciliacion_periodo_mes", "periodo_mes BETWEEN 1 AND 12");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CuentaBancariaId).IsRequired();
        builder.Property(e => e.PeriodoAnio).IsRequired();
        builder.Property(e => e.PeriodoMes).IsRequired();
        builder.Property(e => e.SaldoExtracto).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.SaldoLibros).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        builder.Property(e => e.CerradaPor);
        builder.Property(e => e.CerradaEn);
        builder.Property(e => e.MotivoReapertura).HasMaxLength(500);
        builder.Property(e => e.ReabiertaPor);
        builder.Property(e => e.ReabiertaEn);
        builder.Property(e => e.FirmanteNombre).HasMaxLength(150);
        builder.Property(e => e.FirmantePuesto).HasMaxLength(100);
        builder.Property(e => e.ActaGeneradaEn);

        builder.HasIndex(e => new { e.CuentaBancariaId, e.PeriodoAnio, e.PeriodoMes })
            .HasDatabaseName("ux_conciliacion_cuenta_periodo")
            .IsUnique();

        builder.HasOne<CuentaBancaria>()
            .WithMany()
            .HasForeignKey(e => e.CuentaBancariaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.ConciliacionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(e => e.Bitacora)
            .WithOne()
            .HasForeignKey(b => b.ConciliacionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Bitacora).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
