using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Integraciones.Fiscal.Domain;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core de <see cref="SolicitudDescarga"/>. Tabla
/// <c>integraciones_fiscal.solicitudes_descarga</c>. Lleva la FSM de la
/// solicitud lado Millet (ver enum <see cref="EstadoSolicitudDescarga"/>).
///
/// <para>
/// Índice <c>ix_solicitudes_descarga_next_poll</c> filtrado para el
/// Poller worker (PR-10): trae solo las solicitudes en estado no-terminal
/// con <c>next_poll_at &lt;= now()</c>. Reduce drasticamente el scan
/// cuando hay muchas históricas Cerradas.
/// </para>
///
/// <para>
/// UNIQUE en <c>request_id_externo</c>: el id que FiscalAPI asigna es
/// global y único, no se puede tener 2 filas locales referenciándolo.
/// </para>
/// </summary>
public sealed class SolicitudDescargaConfiguration : IEntityTypeConfiguration<SolicitudDescarga>
{
    public void Configure(EntityTypeBuilder<SolicitudDescarga> builder)
    {
        builder.ToTable("solicitudes_descarga");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.EmpresaId).IsRequired();
        builder.Property(s => s.DownloadRuleId).IsRequired();
        builder.Property(s => s.RequestIdExterno).HasMaxLength(100).IsRequired();
        builder.Property(s => s.StartDate).IsRequired();
        builder.Property(s => s.EndDate).IsRequired();
        builder.Property(s => s.Estado).HasConversion<short>().IsRequired();
        builder.Property(s => s.SatRequestStatusExterno);
        builder.Property(s => s.DownloadRequestStatusExterno);
        builder.Property(s => s.InvoiceCount);
        builder.Property(s => s.LastPollAt);
        builder.Property(s => s.NextPollAt).IsRequired();
        builder.Property(s => s.CosechadaAt);
        builder.Property(s => s.CerradaAt);
        builder.Property(s => s.ErrorCodigo).HasMaxLength(200);
        builder.Property(s => s.ErrorMensaje).HasMaxLength(2000);
        builder.Property(s => s.AttemptsPoll).IsRequired();

        builder.HasIndex(s => s.RequestIdExterno)
            .IsUnique()
            .HasDatabaseName("uq_solicitudes_request_externo");

        // Indice filtrado: solo solicitudes "vivas" que el poller revisa.
        // Estado 2=EsperandoSat, 3=EsperandoApi, 4=Terminada (pendiente cosecha).
        builder.HasIndex(s => new { s.Estado, s.NextPollAt })
            .HasDatabaseName("ix_solicitudes_descarga_next_poll")
            .HasFilter("estado IN (2, 3, 4) AND deleted_at IS NULL");

        builder.HasIndex(s => new { s.EmpresaId, s.DownloadRuleId, s.StartDate })
            .HasDatabaseName("ix_solicitudes_empresa_rule_start");
    }
}
