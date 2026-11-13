using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>M-232: bitácora de pruebas de conexión con el timbrador (aparte de la configuración).</summary>
public sealed class PruebaConexionPacConfiguration : IEntityTypeConfiguration<PruebaConexionPac>
{
    public void Configure(EntityTypeBuilder<PruebaConexionPac> builder)
    {
        builder.ToTable("pruebas_conexion_pac");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.EmpresaId).IsRequired();
        builder.Property(p => p.Proveedor).HasConversion<short>().IsRequired();
        builder.Property(p => p.ConfiguracionPacId).IsRequired();
        builder.Property(p => p.Origen).HasConversion<short>().IsRequired();
        builder.Property(p => p.Exitosa).IsRequired();
        builder.Property(p => p.StatusCode).IsRequired();
        builder.Property(p => p.Mensaje).HasMaxLength(PruebaConexionPac.MensajeMaxLength).IsRequired();
        builder.Property(p => p.RealizadaEn).IsRequired();
        builder.Property(p => p.RealizadaPor).HasMaxLength(200);
        builder.HasIndex(p => new { p.EmpresaId, p.Proveedor, p.RealizadaEn })
            .HasDatabaseName("ix_pruebas_conexion_pac_empresa_proveedor_fecha");
    }
}
