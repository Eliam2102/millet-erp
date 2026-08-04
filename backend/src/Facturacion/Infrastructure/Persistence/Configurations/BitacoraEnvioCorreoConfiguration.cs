using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Envios;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="BitacoraEnvioCorreo"/> (F2-PR2). Tabla
/// <c>facturacion.bitacora_envio_correo</c>. Índice por estado para que el
/// worker barra sólo los pendientes/fallidos.
/// </summary>
public sealed class BitacoraEnvioCorreoConfiguration : IEntityTypeConfiguration<BitacoraEnvioCorreo>
{
    public void Configure(EntityTypeBuilder<BitacoraEnvioCorreo> builder)
    {
        builder.ToTable("bitacora_envio_correo");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.Destinatario).HasMaxLength(254).IsRequired();

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.Intentos).IsRequired();
        builder.Property(e => e.EnviadoAt);
        builder.Property(e => e.UltimoError).HasMaxLength(1000);

        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_bitacora_envio_correo_estado");
        builder.HasIndex(e => e.ComprobanteId)
            .HasDatabaseName("ix_bitacora_envio_correo_comprobante");
    }
}
