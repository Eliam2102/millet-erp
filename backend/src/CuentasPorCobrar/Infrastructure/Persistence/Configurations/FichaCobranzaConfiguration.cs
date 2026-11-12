using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;

namespace Millet.CuentasPorCobrar.Infrastructure.Persistence.Configurations;

public sealed class FichaCobranzaConfiguration : IEntityTypeConfiguration<FichaCobranza>
{
    public void Configure(EntityTypeBuilder<FichaCobranza> builder)
    {
        builder.ToTable("ficha_cobranza");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.FacturaCarteraId).IsRequired();
        builder.Property(e => e.ClienteId).IsRequired();
        builder.Property(e => e.ResponsableNombre).HasMaxLength(200);
        builder.HasOne<FacturaCartera>().WithMany().HasForeignKey(e => e.FacturaCarteraId).OnDelete(DeleteBehavior.Restrict);
        // Una sola ficha por factura.
        builder.HasIndex(e => e.FacturaCarteraId).IsUnique().HasDatabaseName("ux_ficha_cobranza_factura");
        builder.HasIndex(e => new { e.ResponsableId, e.SiguienteRevision }).HasDatabaseName("ix_ficha_cobranza_responsable_revision");
        builder.HasIndex(e => e.ClienteId).HasDatabaseName("ix_ficha_cobranza_cliente");
    }
}

public sealed class FichaCobranzaEventoConfiguration : IEntityTypeConfiguration<FichaCobranzaEvento>
{
    public void Configure(EntityTypeBuilder<FichaCobranzaEvento> builder)
    {
        builder.ToTable("ficha_cobranza_evento", t =>
        {
            t.HasCheckConstraint("ck_ficha_cobranza_evento_tipo", "tipo IN (1, 2, 3, 4, 5, 6)");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();
        builder.Property(e => e.Canal).HasConversion<short?>();
        builder.Property(e => e.Resultado).HasConversion<short?>();
        builder.Property(e => e.UsuarioNombre).HasMaxLength(200);
        builder.Property(e => e.Nota).HasMaxLength(2000).IsRequired();
        builder.Property(e => e.NotaAnterior).HasMaxLength(2000);
        builder.Property(e => e.MotivoCorreccion).HasMaxLength(400);
        builder.Property(e => e.MontoComprometido).HasPrecision(14, 2);
        builder.Property(e => e.ResponsableAntesNombre).HasMaxLength(200);
        builder.Property(e => e.ResponsableDespuesNombre).HasMaxLength(200);
        builder.HasOne<FichaCobranza>().WithMany().HasForeignKey(e => e.FichaCobranzaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.FichaCobranzaId, e.Fecha }).HasDatabaseName("ix_ficha_cobranza_evento_ficha_fecha");
    }
}
