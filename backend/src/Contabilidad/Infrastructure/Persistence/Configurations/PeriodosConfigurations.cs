using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.periodos_contables</c> (F1-CON-03, C1.1): un periodo por (empresa, ejercicio, número 1–13).</summary>
public sealed class PeriodoContableConfiguration : IEntityTypeConfiguration<PeriodoContable>
{
    public void Configure(EntityTypeBuilder<PeriodoContable> builder)
    {
        builder.ToTable("periodos_contables", t =>
        {
            t.HasCheckConstraint("ck_periodos_contables_numero", "numero BETWEEN 1 AND 13");
            t.HasCheckConstraint("ck_periodos_contables_estado", "estado BETWEEN 0 AND 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Estado).HasConversion<short>().IsRequired();
        builder.Property(x => x.CerradoPor).HasMaxLength(200);
        builder.Property(x => x.ReabiertoPor).HasMaxLength(200);
        builder.Ignore(x => x.EsPeriodoAjustes);
        builder.Ignore(x => x.Abierto);
        builder.Ignore(x => x.Etiqueta);
        builder.HasIndex(x => new { x.EmpresaId, x.Ejercicio, x.Numero }).IsUnique().HasDatabaseName("ux_periodos_contables_ejercicio_numero");
    }
}

/// <summary><c>contabilidad.periodos_contables_eventos</c>: historial de crear, cerrar y reabrir. Solo inserciones.</summary>
public sealed class PeriodoContableEventoConfiguration : IEntityTypeConfiguration<PeriodoContableEvento>
{
    public void Configure(EntityTypeBuilder<PeriodoContableEvento> builder)
    {
        builder.ToTable("periodos_contables_eventos", t => t.HasCheckConstraint("ck_periodos_eventos_accion", "accion BETWEEN 0 AND 2"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Accion).HasConversion<short>().IsRequired();
        builder.Property(x => x.Usuario).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Motivo).HasMaxLength(PeriodoContable.MaxMotivo);
        builder.HasIndex(x => new { x.EmpresaId, x.PeriodoId, x.Fecha });
        builder.HasOne<PeriodoContable>().WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
    }
}
