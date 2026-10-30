using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Conciliaciones;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class ExtractoLineaConfiguration : IEntityTypeConfiguration<ExtractoLinea>
{
    public void Configure(EntityTypeBuilder<ExtractoLinea> builder)
    {
        builder.ToTable("extracto_linea", t =>
        {
            t.HasCheckConstraint("ck_extracto_linea_estado", "estado IN (1, 2, 3, 4)");
            t.HasCheckConstraint("ck_extracto_linea_fuente", "fuente IN (1, 2)");
            t.HasCheckConstraint("ck_extracto_linea_importe", "(cargo IS NOT NULL OR abono IS NOT NULL)");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ConciliacionId).IsRequired();
        builder.Property(e => e.NumeroLinea).IsRequired();
        builder.Property(e => e.Fecha).IsRequired();
        builder.Property(e => e.Descripcion).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Referencia).HasMaxLength(120);
        builder.Property(e => e.Cargo).HasPrecision(18, 2);
        builder.Property(e => e.Abono).HasPrecision(18, 2);
        builder.Property(e => e.Fuente).HasConversion<short>().IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.MatchTipo).HasConversion<short>();
        builder.Property(e => e.MatchMovimientoId);
        builder.Property(e => e.LineaHash).HasMaxLength(64);

        builder.Property(e => e.PartidaTransito).IsRequired();
        builder.Property(e => e.PartidaTransitoTipo).HasConversion<short>();
        builder.Property(e => e.PartidaTransitoResponsable).HasMaxLength(150);
        builder.Property(e => e.PartidaTransitoAutorizadaPor);

        var jsonOptions = (JsonSerializerOptions?)null;
        builder.Property(e => e.CandidatosRevisionIds)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, jsonOptions) ?? new List<Guid>(),
                new ValueComparer<List<Guid>>(
                    (c1, c2) => c1 != null && c2 != null ? c1.SequenceEqual(c2) : c1 == c2,
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()))
            .IsRequired();

        builder.HasIndex(e => new { e.ConciliacionId, e.NumeroLinea })
            .HasDatabaseName("ix_extracto_linea_conciliacion_numero");

        builder.HasIndex(e => new { e.ConciliacionId, e.LineaHash })
            .HasDatabaseName("ix_extracto_linea_hash");

        builder.HasIndex(e => e.MatchMovimientoId)
            .HasDatabaseName("ix_extracto_linea_match_movimiento_id");

        builder.HasOne<MovimientoBancario>()
            .WithMany()
            .HasForeignKey(e => e.MatchMovimientoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Matches)
            .WithOne()
            .HasForeignKey(m => m.ExtractoLineaId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Matches).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
