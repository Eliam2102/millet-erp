using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Almacen.Domain.Idempotencia;

namespace Millet.Almacen.Infrastructure.Persistence.Configurations;

/// <summary>EF config de <see cref="MensajeApp"/> en <c>almacen.mensajes_app</c>.</summary>
public sealed class MensajeAppConfiguration : IEntityTypeConfiguration<MensajeApp>
{
    public void Configure(EntityTypeBuilder<MensajeApp> builder)
    {
        builder.ToTable("mensajes_app");
        builder.HasKey(x => x.MensajeId);
        builder.Property(x => x.MensajeId).HasMaxLength(64);
        builder.Property(x => x.Tipo).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Referencia).HasMaxLength(100);
        builder.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.Estado).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Folio).HasMaxLength(100);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.RecibidoAt).IsRequired();
    }
}
