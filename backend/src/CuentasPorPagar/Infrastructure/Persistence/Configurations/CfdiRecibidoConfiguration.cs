using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.CuentasPorPagar.Domain.Cfdi;

namespace Millet.CuentasPorPagar.Infrastructure.Persistence.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="CfdiRecibido"/> (F1-PR1).
/// Tabla <c>cuentas_por_pagar.cfdis_recibidos</c>.
///
/// <para>
/// Índices del §5.1 del 01-diseno:
/// <list type="bullet">
///   <item><c>ux_cfdis_recibidos_uuid</c> — unicidad por UUID a través de canales.</item>
///   <item><c>ix_cfdis_recibidos_estado_fecha</c> — bandeja "por capturar" (índice parcial).</item>
///   <item><c>ix_cfdis_proveedor_total</c> — match heurístico CFDI ↔ OC (índice parcial).</item>
/// </list>
/// </para>
/// </summary>
public sealed class CfdiRecibidoConfiguration : IEntityTypeConfiguration<CfdiRecibido>
{
    public void Configure(EntityTypeBuilder<CfdiRecibido> builder)
    {
        builder.ToTable("cfdis_recibidos");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EmpresaId).IsRequired();

        // VOs serializados a column escalar vía conversion. La unicidad
        // del UUID se enforca con índice único sobre la columna persistida.
        builder.Property(e => e.UuidCfdi)
            .HasConversion(v => v.Valor, s => UuidCfdi.Parse(s))
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(e => e.RfcEmisor)
            .HasConversion(v => v.Valor, s => RfcMexicano.Parse(s))
            .HasMaxLength(13)
            .IsRequired();

        builder.Property(e => e.RfcReceptor)
            .HasConversion(v => v.Valor, s => RfcMexicano.Parse(s))
            .HasMaxLength(13)
            .IsRequired();

        builder.Property(e => e.Tipo)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(e => e.Folio).HasMaxLength(40);
        builder.Property(e => e.Serie).HasMaxLength(25);

        builder.Property(e => e.FechaCfdi).IsRequired();

        builder.Property(e => e.Total).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Subtotal).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.ImpuestosTrasladados).HasPrecision(18, 4).IsRequired();
        builder.Property(e => e.Retenciones).HasPrecision(18, 4).IsRequired();

        builder.Property(e => e.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(e => e.TipoCambio).HasPrecision(18, 6);

        // TES-PR8 [T-G11]: PUE/PPD del Comprobante; NULL en filas históricas.
        builder.Property(e => e.MetodoPago).HasMaxLength(3);

        builder.Property(e => e.CanalOrigen).HasConversion<short>().IsRequired();
        builder.Property(e => e.FechaRecepcion).IsRequired();
        builder.Property(e => e.Estado).HasConversion<short>().IsRequired();

        // PR-12 → PR-14: XmlBlobRef / XmlHash quedan nullable a nivel
        // schema (compatibilidad con filas históricas creadas como
        // MetadataOnly en PR-12). La factory CfdiRecibido.Ingresar exige
        // valores no vacíos — desde PR-14 todo CFDI nuevo tiene XML
        // porque el Poller cosecha con SatQueryType=CFDI.
        builder.Property(e => e.XmlBlobRef).HasMaxLength(400);
        builder.Property(e => e.PdfBlobRef).HasMaxLength(400);
        builder.Property(e => e.XmlHashSha256).HasMaxLength(64);

        builder.Property(e => e.DocumentoDestinoId);
        builder.Property(e => e.MotivoDescarte).HasMaxLength(400);
        builder.Property(e => e.CfdiOriginalId);

        // Trazabilidad al feed de descarga masiva (audit trail).
        builder.Property(e => e.SolicitudDescargaId);
        builder.Property(e => e.RequestIdExternoFiscalApi).HasMaxLength(100);

        // §5.1 índices.
        builder.HasIndex(e => e.UuidCfdi)
            .HasDatabaseName("ux_cfdis_recibidos_uuid")
            .IsUnique();

        builder.HasIndex(e => new { e.Estado, e.FechaRecepcion })
            .HasDatabaseName("ix_cfdis_recibidos_estado_fecha")
            .HasFilter("estado = 1"); // EstadoCfdiRecibido.PorProcesar

        builder.HasIndex(e => new { e.RfcEmisor, e.Total })
            .HasDatabaseName("ix_cfdis_proveedor_total")
            .HasFilter("estado = 1");

        builder.HasIndex(e => e.EmpresaId)
            .HasDatabaseName("ix_cfdis_recibidos_empresa");
    }
}
