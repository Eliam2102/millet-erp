using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Domain.Integraciones;

namespace Millet.Compartido.Infrastructure.Persistence;

/// <summary>I13.6: alertas del tablero de integraciones.</summary>
internal static class TableroIntegracionesModelo
{
    /// <summary>
    /// CHECK del tipo armado desde el enum: un tipo nuevo cambia el modelo y obliga a
    /// crear su migración (QA I13.6 H1: «Reintentos altos» se agregó sin ampliar el CHECK).
    /// </summary>
    internal static readonly string CheckTipo =
        $"tipo IN ({string.Join(", ", Enum.GetValues<TipoAlertaIntegracion>().Select(t => (short)t))})";

    public static void Configure(ModelBuilder modelBuilder)
    {
        var a = modelBuilder.Entity<AlertaIntegracion>();
        a.ToTable("alertas_integracion", t =>
        {
            t.HasCheckConstraint("ck_alertas_integracion_tipo", CheckTipo);
            t.HasCheckConstraint("ck_alertas_integracion_cierre", "cerrada_en IS NULL OR cerrada_en >= abierta_en");
        });
        a.HasKey(x => x.Id);
        a.Property(x => x.Id).ValueGeneratedNever();
        a.Property(x => x.FuenteClave).HasMaxLength(80).IsRequired();
        a.Property(x => x.FuenteNombre).HasMaxLength(200).IsRequired();
        a.Property(x => x.Tipo).HasConversion<short>().IsRequired();
        a.Property(x => x.Detalle).HasMaxLength(AlertaIntegracion.MaximoDetalle).IsRequired();
        a.Ignore(x => x.Abierta);
        // Una sola alerta abierta por integración y tipo.
        a.HasIndex(x => new { x.FuenteClave, x.Tipo }).IsUnique().HasFilter("cerrada_en IS NULL")
            .HasDatabaseName("ux_alertas_integracion_abierta");
        a.HasIndex(x => x.AbiertaEn).HasDatabaseName("ix_alertas_integracion_abierta_en");
    }
}
