using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.SharedKernel.Application.Idempotency;

namespace Millet.SharedKernel.Infrastructure.Idempotency;

/// <summary>
/// Configuración EF de <see cref="IdempotencyKey"/> en
/// <c>core.idempotency_keys</c>. PK compuesta + índices del ADR-0020 §"Tabla".
/// </summary>
internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("idempotency_keys", t =>
        {
            t.HasCheckConstraint(
                "ck_idempotency_keys_status",
                "status IN ('processing', 'completed', 'failed')");
        });

        builder.HasKey(x => new { x.EmpresaId, x.UsuarioId, x.Key });

        builder.Property(x => x.Key).HasColumnType("text").IsRequired();
        builder.Property(x => x.EmpresaId).IsRequired();
        builder.Property(x => x.UsuarioId).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Path).HasColumnType("text").IsRequired();
        builder.Property(x => x.RequestBodyHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ResponseStatusCode);
        builder.Property(x => x.ResponseBody).HasColumnType("jsonb");
        builder.Property(x => x.ResponseBodyTruncated).HasDefaultValue(false);
        builder.Property(x => x.CorrelationId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CompletedAt);

        // Índices del ADR: (status, created_at) para el cleanup job;
        // (created_at) para particionado futuro.
        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_idempotency_keys_status_created_at");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_idempotency_keys_created_at");
    }
}
