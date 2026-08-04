using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Comprobantes;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="BitacoraIntentoTimbrado"/> ([Decisión
/// 01-G] G4). Tabla <c>facturacion.bitacora_intento_timbrado</c>, append-only;
/// índice por comprobante para el historial del detalle.
/// </summary>
public sealed class BitacoraIntentoTimbradoConfiguration : IEntityTypeConfiguration<BitacoraIntentoTimbrado>
{
    public void Configure(EntityTypeBuilder<BitacoraIntentoTimbrado> builder)
    {
        builder.ToTable("bitacora_intento_timbrado");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.ComprobanteId).IsRequired();
        builder.Property(e => e.IntentoNumero).IsRequired();

        builder.Property(e => e.Resultado).HasConversion<short>().IsRequired();
        builder.Property(e => e.ErrorCodigo).HasMaxLength(50);
        builder.Property(e => e.ErrorMensaje).HasMaxLength(2000);
        builder.Property(e => e.RegistradoAt).IsRequired();

        builder.HasIndex(e => new { e.ComprobanteId, e.IntentoNumero })
            .HasDatabaseName("ix_bitacora_intento_timbrado_comprobante");
    }
}
