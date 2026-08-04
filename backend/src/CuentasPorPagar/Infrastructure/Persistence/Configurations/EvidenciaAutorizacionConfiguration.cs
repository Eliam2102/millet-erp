using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Evidencias;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="EvidenciaAutorizacion"/> con
/// CHECK constraint polimórfico (§3.bis.4, §1.4 del 04-cuidados-infra).
///
/// <para>
/// CHECK: <c>(tipo_documento = 1 AND EXISTS factura)</c> — MVP solo
/// soporta <see cref="TipoDocumentoEvidencia.FacturaProveedor"/>. Los
/// otros 3 tipos quedan reservados con <c>PLATFORM-TODO</c> y un CHECK
/// que rechaza cualquier inserción con tipo != 1 hasta que existan los
/// agregados destino.
/// </para>
///
/// <para>
/// Índice compuesto <c>(tipo_documento, documento_id)</c> para queries
/// del tipo "lista evidencias de esta factura": evita escaneo y permite
/// que el endpoint las traiga en O(log n).
/// </para>
/// </summary>
public sealed class EvidenciaAutorizacionConfiguration : IEntityTypeConfiguration<EvidenciaAutorizacion>
{
    public void Configure(EntityTypeBuilder<EvidenciaAutorizacion> builder)
    {
        builder.ToTable("evidencias_autorizacion", t =>
        {
            // PLATFORM-TODO(<EvidenciasParaTodosLosTiposDoc>): cuando F6/F7
            // introduzcan los agregados AnticipoProveedor, NotaCargo y
            // ComprobacionGastos, ampliar este CHECK para validar
            // existencia del FK según el tipo (usando una user-defined
            // function en Postgres o un trigger). MVP restringe a tipo=1
            // (FacturaProveedor) que sí existe en F3-PR1.
            t.HasCheckConstraint(
                "ck_evidencias_autorizacion_tipo_soportado",
                "tipo_documento = 1");

            t.HasCheckConstraint(
                "ck_evidencias_firma_pendiente_requiere_fecha_limite",
                "estado_firma_fisica != 2 OR fecha_limite_firma_fisica IS NOT NULL");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();
        builder.Property(e => e.TipoDocumento).HasConversion<short>().IsRequired();
        builder.Property(e => e.DocumentoId).IsRequired();
        builder.Property(e => e.Tipo).HasConversion<short>().IsRequired();

        builder.Property(e => e.ArchivoBlobRef).HasMaxLength(400).IsRequired();
        builder.Property(e => e.NombreArchivo).HasMaxLength(255).IsRequired();
        builder.Property(e => e.ContentType).HasMaxLength(120).IsRequired();
        builder.Property(e => e.TamanioBytes);

        builder.Property(e => e.Comentario).HasMaxLength(1000).IsRequired();

        builder.Property(e => e.EstadoFirmaFisica).HasConversion<short>().IsRequired();
        builder.Property(e => e.FechaLimiteFirmaFisica);
        builder.Property(e => e.FechaRecepcionFirmaFisica);

        builder.Property(e => e.CapturadoPor);
        builder.Property(e => e.FechaCaptura).IsRequired();

        builder.HasIndex(e => new { e.TipoDocumento, e.DocumentoId })
            .HasDatabaseName("ix_evidencias_autorizacion_documento");

        builder.HasIndex(e => new { e.EstadoFirmaFisica, e.FechaLimiteFirmaFisica })
            .HasDatabaseName("ix_evidencias_firma_pendiente")
            .HasFilter("estado_firma_fisica = 2"); // Pendiente
    }
}
