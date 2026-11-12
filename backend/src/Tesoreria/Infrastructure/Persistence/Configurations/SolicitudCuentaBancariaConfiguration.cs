using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class SolicitudCuentaBancariaConfiguration : IEntityTypeConfiguration<SolicitudCuentaBancaria>
{
    public void Configure(EntityTypeBuilder<SolicitudCuentaBancaria> builder)
    {
        builder.ToTable("solicitudes_cuenta_bancaria", t =>
        {
            t.HasCheckConstraint("ck_solicitudes_cuenta_bancaria_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_solicitudes_cuenta_bancaria_tipo", "tipo IN (1, 2)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.DatosVigentesJson).HasColumnType("jsonb");
        builder.Property(e => e.DatosPropuestosJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.SolicitadoPor).IsRequired();
        builder.Property(e => e.SolicitadoEn).IsRequired();
        builder.Property(e => e.MotivoRechazo).HasMaxLength(400);
        builder.Ignore(e => e.DatosPropuestos);
        builder.Ignore(e => e.DatosVigentes);

        builder.HasIndex(e => new { e.EmpresaId, e.Estado }).HasDatabaseName("ix_solicitudes_cuenta_bancaria_estado");
        // Una sola solicitud pendiente por cuenta, también ante doble clic.
        builder.HasIndex(e => e.CuentaBancariaId).IsUnique()
            .HasFilter("estado = 1 AND cuenta_bancaria_id IS NOT NULL").HasDatabaseName("ux_solicitudes_cuenta_bancaria_pendiente");
    }
}
