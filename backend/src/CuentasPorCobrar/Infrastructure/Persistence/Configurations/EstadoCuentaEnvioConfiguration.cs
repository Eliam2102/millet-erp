using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.EstadoCuenta;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class EstadoCuentaEnvioConfiguration : IEntityTypeConfiguration<EstadoCuentaEnvio>
{
    public void Configure(EntityTypeBuilder<EstadoCuentaEnvio> builder)
    {
        builder.ToTable("estado_cuenta_envio", t =>
        {
            t.HasCheckConstraint("ck_estado_cuenta_envio_estado", "estado IN (1, 2, 3)");
            t.HasCheckConstraint("ck_estado_cuenta_envio_tamano", "tamano_bytes > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.Destinatario).HasMaxLength(320).IsRequired();
        builder.Property(e => e.Mensaje).HasMaxLength(2000);
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.UsuarioNombre).HasMaxLength(200);
        builder.Property(e => e.Fecha).IsRequired();
        builder.Property(e => e.TotalesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.NumeroFilas).IsRequired();
        builder.Property(e => e.PdfSha256).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(e => e.NombreArchivo).HasMaxLength(200).IsRequired();
        builder.Property(e => e.TamanoBytes).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.DetalleError).HasMaxLength(2000);

        // Historial por cliente, más reciente primero.
        builder.HasIndex(e => new { e.ClienteId, e.Fecha })
            .HasDatabaseName("ix_estado_cuenta_envio_cliente_fecha")
            .IsDescending(false, true);
    }
}
