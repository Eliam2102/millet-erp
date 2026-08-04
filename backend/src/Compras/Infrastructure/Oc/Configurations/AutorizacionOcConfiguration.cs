using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain.Oc;

namespace Millet.Compras.Infrastructure.Oc.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="AutorizacionOC"/> (tabla
/// <c>compras.orden_compra_autorizaciones</c>, diseño §10.1).
///
/// <list type="bullet">
///   <item>CHECK <c>ck_oc_autorizaciones_nivel</c>: nivel ∈ {1, 2}.</item>
///   <item>CHECK <c>ck_oc_autorizaciones_resultado</c>: resultado ∈ {1, 2}.</item>
///   <item>CHECK <c>ck_oc_autorizaciones_motivo</c>: si resultado=2
///         (Rechazado), motivo_rechazo_id no null.</item>
///   <item>UNIQUE PARCIAL <c>uq_oc_autorizaciones_autorizado</c>:
///         <c>(orden_compra_id, nivel) WHERE resultado = 1</c> —
///         garantiza una sola autorización exitosa por nivel. Rechazos
///         múltiples permitidos.</item>
///   <item>Índice <c>ix_oc_autorizaciones_usuario</c>.</item>
/// </list>
///
/// La FK al agregado raíz con CASCADE la declara
/// <see cref="OrdenCompraConfiguration"/>.
/// </summary>
public sealed class AutorizacionOcConfiguration : IEntityTypeConfiguration<AutorizacionOC>
{
    public void Configure(EntityTypeBuilder<AutorizacionOC> builder)
    {
        builder.ToTable("orden_compra_autorizaciones", t =>
        {
            t.HasCheckConstraint("ck_oc_autorizaciones_nivel", "nivel IN (1, 2)");
            t.HasCheckConstraint("ck_oc_autorizaciones_resultado", "resultado IN (1, 2)");
            t.HasCheckConstraint("ck_oc_autorizaciones_motivo",
                "resultado = 1 OR (resultado = 2 AND motivo_rechazo_id IS NOT NULL)");
        });

        builder.HasKey(a => a.Id);

        // Id asignado por el dominio con Guid.CreateVersion7(); sin esta
        // declaración, EF Core trata la entity como Modified al insertar
        // (asume que ya existe en DB) → DbUpdateConcurrencyException.
        // Mismo patrón que LineaOrdenCompra/LineaRequisicion.
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.OrdenCompraId).IsRequired();

        builder.Property(a => a.Nivel)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(a => a.Resultado)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(a => a.UsuarioId).IsRequired();
        builder.Property(a => a.FechaHora).IsRequired();

        builder.Property(a => a.MotivoRechazoId);
        builder.Property(a => a.MotivoRechazoTexto).HasColumnType("text");
        builder.Property(a => a.Notas).HasColumnType("text");

        // UNIQUE parcial §10.1: garantiza una sola fila autorizada por
        // (oc, nivel). Rechazos no participan — pueden repetirse.
        builder.HasIndex(a => new { a.OrdenCompraId, a.Nivel })
            .IsUnique()
            .HasFilter("resultado = 1")
            .HasDatabaseName("uq_oc_autorizaciones_autorizado");

        builder.HasIndex(a => a.UsuarioId).HasDatabaseName("ix_oc_autorizaciones_usuario");
    }
}
