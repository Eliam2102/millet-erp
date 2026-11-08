using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.pasos_cierre</c> (CON-12): checklist configurable; una clave por empresa.</summary>
public sealed class PasoCierreContableConfiguration : IEntityTypeConfiguration<PasoCierreContable>
{
    public void Configure(EntityTypeBuilder<PasoCierreContable> builder)
    {
        builder.ToTable("pasos_cierre", t =>
        {
            t.HasCheckConstraint("ck_pasos_cierre_tipo", "tipo BETWEEN 0 AND 1");
            t.HasCheckConstraint("ck_pasos_cierre_alcance", "alcance BETWEEN 0 AND 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Clave).HasMaxLength(PasoCierreContable.ClaveMaxima).IsRequired();
        builder.Property(x => x.Nombre).HasMaxLength(PasoCierreContable.NombreMaximo).IsRequired();
        builder.Property(x => x.Descripcion).HasMaxLength(PasoCierreContable.DescripcionMaxima).IsRequired();
        builder.Property(x => x.Tipo).HasConversion<short>();
        builder.Property(x => x.Alcance).HasConversion<short>();
        builder.HasIndex(x => new { x.EmpresaId, x.Clave }).IsUnique().HasDatabaseName("ux_pasos_cierre_clave");
    }
}

/// <summary><c>contabilidad.marcas_paso_cierre</c>: excepciones autorizadas y confirmaciones de pasos manuales por periodo.</summary>
public sealed class MarcaPasoCierreConfiguration : IEntityTypeConfiguration<MarcaPasoCierre>
{
    public void Configure(EntityTypeBuilder<MarcaPasoCierre> builder)
    {
        builder.ToTable("marcas_paso_cierre", t => t.HasCheckConstraint("ck_marcas_paso_cierre_tipo", "tipo BETWEEN 0 AND 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PasoClave).HasMaxLength(PasoCierreContable.ClaveMaxima).IsRequired();
        builder.Property(x => x.Tipo).HasConversion<short>();
        builder.Property(x => x.Motivo).HasMaxLength(MarcaPasoCierre.MotivoMaximo).IsRequired();
        builder.Property(x => x.UsuarioNombre).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RetiradaPor).HasMaxLength(256);
        builder.Ignore(x => x.Vigente);
        builder.HasIndex(x => new { x.EmpresaId, x.PeriodoId, x.PasoClave, x.Tipo }).IsUnique()
            .HasFilter("retirada_en IS NULL").HasDatabaseName("ux_marcas_paso_cierre_vigente");
        builder.HasOne<PeriodoContable>().WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary><c>contabilidad.cierres_periodo</c>: foto del checklist en cada cierre (solo inserción).</summary>
public sealed class CierrePeriodoRegistroConfiguration : IEntityTypeConfiguration<CierrePeriodoRegistro>
{
    public void Configure(EntityTypeBuilder<CierrePeriodoRegistro> builder)
    {
        builder.ToTable("cierres_periodo");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CerradoPor).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Checklist).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.EmpresaId, x.PeriodoId, x.CerradoEn });
        builder.HasOne<PeriodoContable>().WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
    }
}
