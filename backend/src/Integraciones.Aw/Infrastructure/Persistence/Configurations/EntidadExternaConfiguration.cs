using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="EntidadExterna"/>. Tabla
/// <c>integraciones_aw.entidad_externa</c>. Índices y constraint de
/// unicidad según <c>docs/integration/02-edi-correlation.md</c> §3.1.
///
/// <para>
/// Cross-schema FKs lógicas (no físicas — convención del monolito,
/// idéntica a Compras §10.1): los módulos no declaran FK física a tablas
/// de otros esquemas para preservar la independencia de los aggregates.
/// Integridad referencial se mantiene a nivel app vía resolvers y
/// validaciones en commands.
/// <list type="bullet">
///   <item><c>empresa_id</c> → <c>compartido.empresa(id)</c> (lógica).</item>
///   <item><c>submitted_by_spn_id</c> → <c>identidad.usuario_servicio(id)</c> (lógica).</item>
/// </list>
/// </para>
/// </summary>
public sealed class EntidadExternaConfiguration : IEntityTypeConfiguration<EntidadExterna>
{
    public void Configure(EntityTypeBuilder<EntidadExterna> builder)
    {
        builder.ToTable("entidad_externa");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.TipoEntidad).HasConversion<short>().IsRequired();
        builder.Property(e => e.ReferenciaExterna).HasMaxLength(40).IsRequired();
        builder.Property(e => e.EmpresaId).IsRequired();
        // Sucursal — código corto (3 chars). HasMaxLength(10) deja margen para
        // posibles códigos legacy de 4-5 chars sin requerir migration adicional.
        builder.Property(e => e.Sucursal).HasMaxLength(10).IsRequired();
        builder.Property(e => e.PayloadOriginal).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.PayloadBlobId);
        builder.Property(e => e.EdiContent).HasColumnType("text");
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();
        builder.Property(e => e.SubmittedAt).IsRequired();
        builder.Property(e => e.DeliveredToAwAt);
        builder.Property(e => e.CorrelatedAt);
        builder.Property(e => e.AwDocId);
        builder.Property(e => e.AwDocIdSecondary).HasMaxLength(80);
        builder.Property(e => e.SubmittedBySpnId);
        builder.Property(e => e.LastError).HasColumnType("text");
        builder.Property(e => e.RetryCount).IsRequired();
        builder.Property(e => e.ResolutionNote).HasColumnType("text");

        // PR #198 — outcome de A+W reportado por el drop service "per-EDI".
        // Strings sueltos (no JSONB) por simplicidad y consistencia con
        // LastError/ResolutionNote. Si surge necesidad de queries
        // estructurados sobre códigos, migrar a text[] en PR posterior.
        builder.Property(e => e.AwErrorCodes).HasMaxLength(200);
        builder.Property(e => e.AwErrorMessage).HasColumnType("text");
        builder.Property(e => e.AwDiagnosticLog).HasColumnType("text");

        // PDF de A+W (oferta/pedido) descargado por AwDocumentSyncWorker.
        builder.Property(e => e.PdfBlobUrl).HasColumnType("text");
        builder.Property(e => e.PdfFilename).HasMaxLength(120);
        builder.Property(e => e.PdfUploadedAt);

        // Unicidad de negocio: una entidad por (tipo, referencia, empresa).
        builder.HasIndex(e => new { e.TipoEntidad, e.ReferenciaExterna, e.EmpresaId })
            .IsUnique()
            .HasDatabaseName("uq_entidad_externa_tipo_referencia_empresa");

        // Índice para bandejas de operación: entidades activas pendientes
        // de atención. PR #201 retiró AwaitingCorrelation del filtro — el
        // flow per-EDI nunca persiste ese estado; las filas legacy se
        // remediaron a FailedCorrelation (estado 4) en la migración. Los
        // estados activos hoy son: 0=Submitted (pre-drop), 3=FailedDrop
        // (drop terminal, reintentable), 4=FailedCorrelation (A+W rechazó
        // o timeout, requiere intervención).
        builder.HasIndex(e => new { e.Estado, e.SubmittedAt })
            .HasDatabaseName("ix_entidad_externa_pendientes")
            .HasFilter("estado IN (0, 3, 4)");

        // Índice inverso: para consultar por aw_doc_id cuando ya está
        // correlacionado (ej. PR D endpoint "ver detalle por AwDocId").
        builder.HasIndex(e => new { e.TipoEntidad, e.AwDocId })
            .HasDatabaseName("ix_entidad_externa_aw_doc")
            .HasFilter("aw_doc_id IS NOT NULL");

        // FK lógicas: empresa_id y submitted_by_spn_id NO declaran FK
        // física en el modelo EF (convención de monolito modular, ver
        // §10.1 de Compras). Integridad referencial a nivel app:
        // - empresa_id: validado por ICurrentEmpresaContext (el contexto
        //   garantiza que la empresa existe antes de aceptar el command).
        // - submitted_by_spn_id: auditoría, no hay lookup en runtime.

        // Índice por empresa_id para los queries de bandeja del módulo.
        builder.HasIndex(e => e.EmpresaId)
            .HasDatabaseName("ix_entidad_externa_empresa_id");

        // Candidatas del AwDocumentSyncWorker: correlacionadas (estado=2),
        // con aw_doc_id, todavía sin PDF adjunto. Partial index pequeño que
        // se vacía a medida que se adjuntan los PDF. Se usa el overload por
        // nombres (no la lambda) porque ya existe otro índice sobre el mismo
        // par de columnas (ix_entidad_externa_aw_doc) — la lambda lo
        // reconfiguraría en vez de crear uno nuevo.
        builder.HasIndex(
                [nameof(EntidadExterna.TipoEntidad), nameof(EntidadExterna.AwDocId)],
                "ix_entidad_externa_pdf_pendiente")
            .HasDatabaseName("ix_entidad_externa_pdf_pendiente")
            .HasFilter("estado = 2 AND aw_doc_id IS NOT NULL AND pdf_blob_url IS NULL");
    }
}
