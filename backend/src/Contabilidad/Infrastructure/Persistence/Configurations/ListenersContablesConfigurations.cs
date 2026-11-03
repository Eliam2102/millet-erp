using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Contabilidad.Domain;

namespace Millet.Contabilidad.Infrastructure.Persistence.Configurations;

/// <summary><c>contabilidad.eventos_procesados</c> (C1.5): dedupe de los listeners por (evento, tipo).</summary>
public sealed class EventoContableProcesadoConfiguration : IEntityTypeConfiguration<EventoContableProcesado>
{
    public void Configure(EntityTypeBuilder<EventoContableProcesado> builder)
    {
        builder.ToTable("eventos_procesados");
        builder.HasKey(x => new { x.EventoId, x.EventoTipo });
        builder.Property(x => x.EventoTipo).HasMaxLength(200).IsRequired();
    }
}

/// <summary><c>contabilidad.cobros_efectivo_caja</c> (C1.5 #11b): cobros en efectivo por sesión hasta su liquidación.</summary>
public sealed class CobroEfectivoCajaConfiguration : IEntityTypeConfiguration<CobroEfectivoCaja>
{
    public void Configure(EntityTypeBuilder<CobroEfectivoCaja> builder)
    {
        builder.ToTable("cobros_efectivo_caja");
        builder.HasKey(x => x.CobroId);
        builder.Property(x => x.CobroId).ValueGeneratedNever();
        builder.Property(x => x.Importe).HasPrecision(18, 2);
        builder.Property(x => x.Estado).HasConversion<short>();
        builder.HasIndex(x => new { x.EmpresaId, x.CajaSesionId, x.Estado }).HasDatabaseName("ix_cobros_efectivo_caja_sesion");
    }
}

/// <summary><c>contabilidad.consumos_contabilizados</c> (C1.5 #14/#15a): cuenta y centro de costo de cada línea de salida (V64).</summary>
public sealed class ConsumoContabilizadoConfiguration : IEntityTypeConfiguration<ConsumoContabilizado>
{
    public void Configure(EntityTypeBuilder<ConsumoContabilizado> builder)
    {
        builder.ToTable("consumos_contabilizados");
        builder.HasKey(x => x.LineaSalidaId);
        builder.Property(x => x.LineaSalidaId).ValueGeneratedNever();
        builder.Property(x => x.Monto).HasPrecision(18, 2);
        builder.HasIndex(x => new { x.EmpresaId, x.SalidaId }).HasDatabaseName("ix_consumos_contabilizados_salida");
    }
}
