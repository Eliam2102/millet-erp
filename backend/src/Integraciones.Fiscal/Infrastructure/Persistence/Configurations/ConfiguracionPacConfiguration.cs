using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="ConfiguracionPac"/>. Tabla
/// <c>integraciones_fiscal.configuracion_pac</c>. UNIQUE
/// <c>(empresa_id, proveedor)</c> garantiza una sola fila activa por
/// vendor por empresa.
///
/// <para>
/// <b>Cross-schema FK lógica</b> (no física, convención del monolito):
/// <c>empresa_id</c> → <c>compartido.empresa(id)</c>. Integridad
/// referencial vía resolvers en commands.
/// </para>
/// </summary>
public sealed class ConfiguracionPacConfiguration : IEntityTypeConfiguration<ConfiguracionPac>
{
    public void Configure(EntityTypeBuilder<ConfiguracionPac> builder)
    {
        builder.ToTable("configuracion_pac", t =>
        {
            t.HasCheckConstraint("ck_configuracion_pac_proveedor",
                "proveedor BETWEEN 1 AND 1"); // hoy solo FiscalAPI (=1)
        });
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.EmpresaId).IsRequired();
        builder.Property(c => c.Proveedor).HasConversion<short>().IsRequired();
        builder.Property(c => c.BaseUrl).HasMaxLength(500).IsRequired();

        // ApiKey cifrado: bytea, sin longitud máxima (DataProtection puede
        // producir ciphertexts de tamaño variable según el plaintext).
        builder.Property(c => c.ApiKeyCifrado).HasColumnType("bytea").IsRequired();

        // SHA256 hex = 64 chars fijos.
        builder.Property(c => c.ApiKeyHash).HasMaxLength(64).IsRequired();

        // PR-13: timeouts/retry/CB y schedule eliminados del agregado.
        // El SDK NuGet maneja resiliencia; los workers nuevos toman
        // intervalos de configuración global.
        builder.Property(c => c.Activo).IsRequired();

        builder.Property(c => c.UltimaRotacionAt);
        builder.Property(c => c.UltimaTestConexionAt);
        builder.Property(c => c.UltimaTestConexionExitosa);

        // CSD del emisor cifrado (ADR-0037, mismo esquema que ApiKey). Las
        // tres piezas + hash son NULL hasta que el admin las capture; la
        // emisión por valores exige CsdConfigurado.
        builder.Property(c => c.CsdCertificadoCifrado).HasColumnType("bytea");
        builder.Property(c => c.CsdLlavePrivadaCifrada).HasColumnType("bytea");
        builder.Property(c => c.CsdPasswordCifrado).HasColumnType("bytea");
        builder.Property(c => c.CsdHash).HasMaxLength(64);
        builder.Property(c => c.CsdActualizadoAt);

        // Identidades de prueba (LCO sintética) — owned opcionales, table
        // splitting sobre la misma fila. Todas las columnas NULL ⇒ navegación
        // null. Solo existen con BaseUrl de sandbox (invariante del agregado).
        builder.OwnsOne(c => c.EmisorSandbox, e =>
        {
            e.Property(i => i.Rfc).HasColumnName("sandbox_emisor_rfc").HasMaxLength(13);
            e.Property(i => i.RazonSocial).HasColumnName("sandbox_emisor_razon_social").HasMaxLength(254);
            e.Property(i => i.RegimenFiscal).HasColumnName("sandbox_emisor_regimen_fiscal").HasMaxLength(10);
            e.Property(i => i.CodigoPostal).HasColumnName("sandbox_emisor_codigo_postal").HasMaxLength(5);
        });
        builder.OwnsOne(c => c.ReceptorSandbox, r =>
        {
            r.Property(i => i.Rfc).HasColumnName("sandbox_receptor_rfc").HasMaxLength(13);
            r.Property(i => i.RazonSocial).HasColumnName("sandbox_receptor_razon_social").HasMaxLength(254);
            r.Property(i => i.RegimenFiscal).HasColumnName("sandbox_receptor_regimen_fiscal").HasMaxLength(10);
            r.Property(i => i.CodigoPostal).HasColumnName("sandbox_receptor_codigo_postal").HasMaxLength(5);
        });

        // UNIQUE (empresa_id, proveedor) — una sola configuración activa
        // por vendor por empresa.
        builder.HasIndex(c => new { c.EmpresaId, c.Proveedor })
            .IsUnique()
            .HasDatabaseName("uq_configuracion_pac_empresa_proveedor");

        // Índice para el worker que itera configuraciones activas.
        builder.HasIndex(c => c.Activo)
            .HasDatabaseName("ix_configuracion_pac_activo")
            .HasFilter("activo = true AND deleted_at IS NULL");
    }
}
