using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.Infrastructure.Persistence.Configurations;

public sealed class CorridaPagoConfiguration : IEntityTypeConfiguration<CorridaPago>
{
    public void Configure(EntityTypeBuilder<CorridaPago> builder)
    {
        builder.ToTable("corrida_pago", t =>
        {
            t.HasCheckConstraint("ck_corrida_pago_estado", "estado IN (1, 2, 3, 4, 5, 6, 7)");
            t.HasCheckConstraint("ck_corrida_pago_total_no_negativo", "total >= 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.CuentaBancariaId).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.Total).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.SolicitadaPor).IsRequired();
        builder.Property(e => e.AutorizadaPor);
        builder.Property(e => e.OficioGeneradoEn);
        builder.Property(e => e.CreadaEn).IsRequired();
        builder.Property(e => e.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(e => e.AutorizadaEn);
        builder.Property(e => e.FirmasRequeridas).IsRequired();
        builder.Property(e => e.RolRequerido).HasMaxLength(60);
        builder.Property(e => e.FirmasRecibidas).IsRequired();
        builder.Property(e => e.MotivoCierre).HasMaxLength(400);
        builder.Property(e => e.Avisos);

        builder.HasMany(e => e.Firmas)
            .WithOne()
            .HasForeignKey(f => f.CorridaId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Firmas).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<CuentaBancaria>()
            .WithMany()
            .HasForeignKey(e => e.CuentaBancariaId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Lineas)
            .WithOne()
            .HasForeignKey(l => l.CorridaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Lineas)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(e => new { e.EmpresaId, e.Estado })
            .HasDatabaseName("ix_corrida_pago_empresa_estado");
    }
}

public sealed class CorridaPagoLineaConfiguration : IEntityTypeConfiguration<CorridaPagoLinea>
{
    public void Configure(EntityTypeBuilder<CorridaPagoLinea> builder)
    {
        builder.ToTable("corrida_pago_linea", t =>
        {
            t.HasCheckConstraint("ck_corrida_pago_linea_importe_positivo", "importe_programado > 0");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.CorridaId).IsRequired();
        builder.Property(e => e.FacturaProveedorId).IsRequired();
        builder.Property(e => e.ImporteProgramado).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.ProveedorId).IsRequired();
        builder.Property(e => e.Bruto).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.Ejecutada).IsRequired();
        builder.Property(e => e.Viva).IsRequired();
        builder.Property(e => e.ReferenciaBancaria).HasMaxLength(120);
        builder.Property(e => e.FechaEjecucion);

        builder.HasIndex(e => new { e.CorridaId, e.FacturaProveedorId })
            .HasDatabaseName("ux_corrida_pago_linea_factura")
            .IsUnique();

        // Un pasivo no puede estar en dos corridas vivas (carrera incluida).
        builder.HasIndex(e => e.FacturaProveedorId)
            .HasDatabaseName("ux_corrida_pago_linea_factura_viva")
            .HasFilter("viva")
            .IsUnique();
    }
}

public sealed class CorridaPagoFirmaConfiguration : IEntityTypeConfiguration<CorridaPagoFirma>
{
    public void Configure(EntityTypeBuilder<CorridaPagoFirma> builder)
    {
        builder.ToTable("corrida_pago_firma");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.CorridaId).IsRequired();
        builder.Property(e => e.UsuarioId).IsRequired();
        builder.Property(e => e.Roles).HasMaxLength(400).IsRequired();
        builder.Property(e => e.FirmadaEn).IsRequired();
        builder.HasIndex(e => new { e.CorridaId, e.UsuarioId })
            .HasDatabaseName("ux_corrida_pago_firma_usuario")
            .IsUnique();
    }
}

public sealed class PoliticaFirmaCorridaConfiguration : IEntityTypeConfiguration<PoliticaFirmaCorrida>
{
    public void Configure(EntityTypeBuilder<PoliticaFirmaCorrida> builder)
    {
        builder.ToTable("corrida_pago_politica_firma", t =>
        {
            t.HasCheckConstraint("ck_corrida_politica_firmas", "firmas_requeridas BETWEEN 1 AND 10 AND monto_desde >= 0");
        });
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.MontoDesde).HasPrecision(18, 2).IsRequired();
        builder.Property(e => e.FirmasRequeridas).IsRequired();
        builder.Property(e => e.RolRequerido).HasMaxLength(60);
        builder.HasIndex(e => new { e.EmpresaId, e.Moneda, e.MontoDesde })
            .HasDatabaseName("ux_corrida_politica_escalon")
            .IsUnique();
    }
}
