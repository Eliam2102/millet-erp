using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.NotaCargo;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

public sealed class NotaCargoConfiguration : IEntityTypeConfiguration<NotaCargo>
{
    public void Configure(EntityTypeBuilder<NotaCargo> builder)
    {
        builder.ToTable("notas_cargo", t =>
        {
            t.HasCheckConstraint("ck_ncg_monto_positivo", "monto > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();

        builder.Property(e => e.Folio)
            .HasConversion(v => v.Valor, s => FolioInternoNotaCargo.Parse(s))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.FolioAnio).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.SucursalId);

        builder.Property(e => e.Concepto).HasMaxLength(400).IsRequired();
        builder.Property(e => e.ConceptoContableId);

        builder.Property(e => e.Monto).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(18, 6);

        builder.Property(e => e.FacturaOrigenId);
        builder.Property(e => e.DevolucionAProveedorId);

        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.NotaCreditoProveedorId);

        builder.Property(e => e.CreadoPor);
        builder.Property(e => e.AutorizadoPor);
        builder.Property(e => e.AplicadoPor);

        builder.Property(e => e.FechaCreacion).IsRequired();
        builder.Property(e => e.FechaAutorizacion);
        builder.Property(e => e.FechaAplicacion);
        builder.Property(e => e.FechaFormalizacion);
        builder.Property(e => e.FechaCancelacion);
        builder.Property(e => e.MotivoCancelacion).HasMaxLength(400);

        builder.HasIndex(e => new { e.EmpresaId, e.FolioAnio, e.Folio })
            .HasDatabaseName("ux_notas_cargo_folio")
            .IsUnique();

        builder.HasIndex(e => new { e.ProveedorId, e.Estado })
            .HasDatabaseName("ix_notas_cargo_proveedor_estado");

        builder.HasIndex(e => e.FacturaOrigenId)
            .HasDatabaseName("ix_notas_cargo_factura_origen")
            .HasFilter("factura_origen_id IS NOT NULL");
    }
}

public sealed class FolioSecuenciaNotaCargoConfiguration : IEntityTypeConfiguration<FolioSecuenciaNotaCargo>
{
    public void Configure(EntityTypeBuilder<FolioSecuenciaNotaCargo> builder)
    {
        builder.ToTable("folio_secuencias_nota_cargo");
        builder.HasKey(e => new { e.EmpresaId, e.Anio });
        builder.Property(e => e.EmpresaId);
        builder.Property(e => e.Anio);
        builder.Property(e => e.Siguiente);
    }
}
