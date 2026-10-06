using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.ejercicios_contables</c> (F1-CON-03, D1): un ejercicio por año y empresa.</summary>
public sealed class EjercicioContableConfiguration : IEntityTypeConfiguration<EjercicioContable>
{
    public void Configure(EntityTypeBuilder<EjercicioContable> builder)
    {
        builder.ToTable("ejercicios_contables", t => t.HasCheckConstraint("ck_ejercicios_contables_anio", "anio BETWEEN 2000 AND 2999"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.EmpresaId, x.Anio }).IsUnique().HasDatabaseName("ux_ejercicios_contables_anio");
    }
}

/// <summary>
/// <c>contabilidad.periodos_contables</c> (D2/D3): 13 por ejercicio. El índice por fechas resuelve fecha → periodo; el 13 tiene
/// las mismas fechas que el último día del 12 y se excluye por número, nunca por fecha.
/// </summary>
public sealed class PeriodoContableConfiguration : IEntityTypeConfiguration<PeriodoContable>
{
    public void Configure(EntityTypeBuilder<PeriodoContable> builder)
    {
        builder.ToTable("periodos_contables", t =>
        {
            t.HasCheckConstraint("ck_periodos_contables_numero", "numero BETWEEN 1 AND 13");
            t.HasCheckConstraint("ck_periodos_contables_estado", "estado BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_periodos_contables_fechas", "fecha_fin >= fecha_inicio");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Estado).HasConversion<short>().IsRequired();
        builder.Property(x => x.AbiertoPor).HasMaxLength(256);
        builder.Property(x => x.CerradoPor).HasMaxLength(256);
        builder.Property(x => x.ReabiertoPor).HasMaxLength(256);
        builder.Ignore(x => x.EsAjuste);
        builder.Ignore(x => x.Clave);
        builder.HasIndex(x => new { x.EmpresaId, x.Anio, x.Numero }).IsUnique().HasDatabaseName("ux_periodos_contables_numero");
        builder.HasIndex(x => new { x.EmpresaId, x.FechaInicio, x.FechaFin });
        builder.HasOne<EjercicioContable>().WithMany().HasForeignKey(x => x.EjercicioId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// <c>contabilidad.periodos_contables_bitacora</c> (D6): solo inserción. El índice único (periodo, versión resultante) es la última
/// defensa contra registrar dos veces la misma transición (D5).
/// </summary>
public sealed class PeriodoContableBitacoraConfiguration : IEntityTypeConfiguration<PeriodoContableBitacora>
{
    public void Configure(EntityTypeBuilder<PeriodoContableBitacora> builder)
    {
        builder.ToTable("periodos_contables_bitacora", t =>
        {
            t.HasCheckConstraint("ck_periodos_contables_bitacora_accion", "accion BETWEEN 1 AND 3");
            t.HasCheckConstraint("ck_periodos_contables_bitacora_motivo", "motivo IS NULL OR char_length(motivo) <= 500");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Accion).HasConversion<short>().IsRequired();
        builder.Property(x => x.EstadoAnterior).HasConversion<short>().IsRequired();
        builder.Property(x => x.EstadoNuevo).HasConversion<short>().IsRequired();
        builder.Property(x => x.Motivo).HasMaxLength(500);
        builder.Property(x => x.UsuarioNombre).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.PeriodoId, x.VersionResultante }).IsUnique().HasDatabaseName("ux_periodos_contables_bitacora_version");
        builder.HasOne<PeriodoContable>().WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
    }
}
