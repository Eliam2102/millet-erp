using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Millet.SharedKernel.Infrastructure.Correo;

/// <summary>Configuración EF de <see cref="CorreoEnvio"/> en <c>core.correo_envio</c> (86e3txv0n).</summary>
internal sealed class CorreoEnvioConfiguration : IEntityTypeConfiguration<CorreoEnvio>
{
    public void Configure(EntityTypeBuilder<CorreoEnvio> builder)
    {
        builder.ToTable("correo_envio", t => t.HasCheckConstraint(
            "ck_correo_envio_estado", "estado IN ('aceptado', 'error', 'simulado')"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Modulo).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ParaOriginal).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Para).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Asunto).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Estado).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Codigo).HasMaxLength(60);
        builder.Property(x => x.Motivo).HasMaxLength(1000);
        builder.Property(x => x.Proveedor).HasMaxLength(20).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.Modulo, x.ReferenciaId }).HasDatabaseName("ix_correo_envio_modulo_referencia");
        builder.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_correo_envio_created_at");
    }
}
