using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>Tabla <c>integraciones_aw.retorno_pendiente</c> (M-160): avisos a A+W que esperan a que vuelva.</summary>
public sealed class AwRetornoPendienteConfiguration : IEntityTypeConfiguration<AwRetornoPendiente>
{
    public void Configure(EntityTypeBuilder<AwRetornoPendiente> builder)
    {
        builder.ToTable("retorno_pendiente");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.TipoEvento).HasMaxLength(150).IsRequired();
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.UltimoError).HasMaxLength(AwRetornoPendiente.MaxError);
        builder.Property(e => e.Estado).HasMaxLength(20).IsRequired();
        builder.HasIndex(e => new { e.Estado, e.ProximoIntentoUtc }).HasDatabaseName("ix_retorno_pendiente_estado_proximo");
    }
}
