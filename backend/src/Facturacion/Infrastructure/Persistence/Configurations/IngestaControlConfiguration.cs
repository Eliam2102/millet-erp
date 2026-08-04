using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Facturacion.Domain.Ingesta;

namespace Millet.Facturacion.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="IngestaControl"/> (F3-PR1). Tabla
/// <c>facturacion.ingesta_control</c>. Único por <c>(origen, clave_natural)</c>
/// — la fuente de verdad de la idempotencia de la ingesta (§5.1 diseño).
/// </summary>
public sealed class IngestaControlConfiguration : IEntityTypeConfiguration<IngestaControl>
{
    public void Configure(EntityTypeBuilder<IngestaControl> builder)
    {
        builder.ToTable("ingesta_control");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Origen).HasConversion<short>().IsRequired();
        builder.Property(e => e.ClaveNatural).HasMaxLength(60).IsRequired();
        builder.Property(e => e.HashContenido).HasMaxLength(64).IsRequired();
        builder.Property(e => e.UltimaVersionAplicada).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.PedidoFacturableId);
        builder.Property(e => e.UltimaLecturaAt).IsRequired();

        // ADR-0048 D3 (PR5): write-back pendiente hacia la tabla-puente.
        builder.Property(e => e.WriteBackPendiente).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.WriteBackSolicitudId);
        builder.Property(e => e.WriteBackResultado).HasConversion<short?>();
        builder.Property(e => e.WriteBackEstado).HasMaxLength(20);
        builder.Property(e => e.WriteBackUuid).HasMaxLength(36);
        builder.Property(e => e.WriteBackMotivo).HasMaxLength(500);
        builder.Property(e => e.WriteBackIntentos).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.WriteBackUltimoError).HasMaxLength(500);
        builder.Property(e => e.WriteBackAt);

        builder.HasIndex(e => new { e.Origen, e.ClaveNatural })
            .IsUnique()
            .HasDatabaseName("ix_ingesta_control_origen_clave");
        builder.HasIndex(e => e.Estado)
            .HasDatabaseName("ix_ingesta_control_estado");
        // Índice parcial: el worker barre SOLO pendientes.
        builder.HasIndex(e => e.WriteBackPendiente)
            .HasDatabaseName("ix_ingesta_control_write_back_pendiente")
            .HasFilter("write_back_pendiente");
    }
}
