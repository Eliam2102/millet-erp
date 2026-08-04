using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Millet.Compras.Domain;

namespace Millet.Compras.Infrastructure.Configurations;

/// <summary>
/// Configuración EF Core para <see cref="Requisicion"/> (tabla
/// <c>compras.requisiciones</c>, diseño §10.1). Aplica:
///
/// - Tipos numéricos: enums como <c>smallint</c> (§10.2.1).
/// - CHECK constraints: <c>estado BETWEEN 0 AND 9</c> (ADR-0043: +2
///   terminales de cierre manual), <c>clasificacion BETWEEN 0 AND 3</c>,
///   <c>prioridad BETWEEN 0 AND 2</c>.
/// - UNIQUE: <c>(empresa_id, folio_anio, folio)</c> — anti-colisión de folios.
/// - 3 índices del §10.1 para queries de bandeja y de departamento.
/// - VO <see cref="Folio"/> mapeado vía <c>HasConversion</c>.
///
/// Soft locks de Capa 2 (ADR-0012) cubiertos por el ComprasHub —
/// ViewingResource/EditingResource sobre la cadena <c>"Requisicion"</c>
/// con el id de esta tabla emiten presence al grupo de la empresa. Capa 1
/// (<c>Version</c> / <c>IsConcurrencyToken</c>) sigue siendo la protección
/// final contra lost updates.
/// </summary>
public sealed class RequisicionConfiguration : IEntityTypeConfiguration<Requisicion>
{
    public void Configure(EntityTypeBuilder<Requisicion> builder)
    {
        builder.ToTable("requisiciones", t =>
        {
            t.HasCheckConstraint("ck_requisiciones_estado", "estado BETWEEN 0 AND 9");
            t.HasCheckConstraint("ck_requisiciones_clasificacion", "clasificacion BETWEEN 0 AND 3");
            t.HasCheckConstraint("ck_requisiciones_prioridad", "prioridad BETWEEN 0 AND 2");
            t.HasCheckConstraint("ck_requisiciones_origen", "origen BETWEEN 0 AND 1");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.EmpresaId).IsRequired();

        // VO Folio → string. El value comparer del record cubre la
        // comparación por valor automáticamente.
        builder.Property(r => r.Folio)
            .HasConversion(
                f => f.Valor,
                v => Folio.Parse(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.FolioAnio).IsRequired();

        builder.Property(r => r.Clasificacion)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(r => r.SucursalId).IsRequired();
        builder.Property(r => r.DepartamentoId).IsRequired();
        // PR3: nullable. RQ manual = null; solo el reorden (RQ Sistema) lo puebla.
        builder.Property(r => r.AlmacenDestinoId);
        builder.Property(r => r.RequisitanteId).IsRequired();
        builder.Property(r => r.CreadorId).IsRequired();

        builder.Property(r => r.Descripcion).HasMaxLength(500);

        builder.Property(r => r.Prioridad)
            .HasConversion<short>()
            .IsRequired();

        builder.Property(r => r.FechaSolicitud).IsRequired();
        builder.Property(r => r.FechaEntregaDeseada);

        builder.Property(r => r.ProveedorSugeridoId);

        builder.Property(r => r.Estado)
            .HasConversion<short>()
            .IsRequired();

        // ADR-0047 PR5.B: origen de la RQ (Manual=0 default / Sistema=1). DEFAULT en
        // BD para que toda RQ existente/manual quede Manual sin backfill explícito.
        builder.Property(r => r.Origen)
            .HasConversion<short>()
            .HasDefaultValue(OrigenRequisicion.Manual)
            .IsRequired();

        builder.Property(r => r.MotivoTerminacionId);
        builder.Property(r => r.MotivoTerminacionTexto).HasMaxLength(500);
        builder.Property(r => r.ActorTerminacionId);
        builder.Property(r => r.FechaTerminacion);

        // F4-PR1 (OC): compromiso exclusivo con OC activa. FK lógica a
        // compras.ordenes_compra(id); intencionalmente NO se declara FK
        // física para preservar la independencia del agregado RQ del
        // submódulo OC. El listener cross-aggregate (F4-PR2) la mantiene
        // sincronizada vía eventos in-process.
        builder.Property(r => r.ComprometidaEnOcId);

        // UNIQUE: anti-colisión de folios por (empresa, año).
        builder.HasIndex(r => new { r.EmpresaId, r.FolioAnio, r.Folio })
            .IsUnique();

        // Índices del §10.1 para queries de bandeja.
        builder.HasIndex(r => new { r.EmpresaId, r.Estado, r.FechaSolicitud })
            .IsDescending(false, false, true);
        builder.HasIndex(r => new { r.EmpresaId, r.DepartamentoId, r.Estado });
        builder.HasIndex(r => new { r.EmpresaId, r.RequisitanteId });

        // F8-PR3: índices con FechaSolicitud DESC built-in para evitar
        // Sort post-IndexScan en bandejas filtradas por depto o
        // requisitante (EXPLAIN ANALYZE pre-merge mostraba quicksort
        // sobre 25 kB para los casos con depto). Bench actual con 10k
        // RQs cumple SLO sin estos índices, pero al crecer la tabla en
        // producción el costo del sort sube linealmente.
        builder.HasIndex(r => new { r.EmpresaId, r.DepartamentoId, r.FechaSolicitud })
            .IsDescending(false, false, true);
        builder.HasIndex(r => new { r.EmpresaId, r.RequisitanteId, r.FechaSolicitud })
            .IsDescending(false, false, true);

        // F4-PR1 (OC) §10.2: índices parciales para el compromiso
        // exclusivo. ix_requisiciones_comprometida acelera el lookup
        // de RQs en una OC específica. ix_requisiciones_disponibles
        // acelera el selector del módulo OC (filtra Autorizadas no
        // comprometidas, por sucursal).
        builder.HasIndex(r => new { r.EmpresaId, r.ComprometidaEnOcId })
            .HasFilter("comprometida_en_oc_id IS NOT NULL")
            .HasDatabaseName("ix_requisiciones_comprometida");

        builder.HasIndex(r => new { r.EmpresaId, r.SucursalId, r.Estado })
            .HasFilter("comprometida_en_oc_id IS NULL")
            .HasDatabaseName("ix_requisiciones_disponibles");

        // Lineas como collection navigation: FK física requisicion_id
        // (cuidado §1.4 OK por ser misma schema). ON DELETE CASCADE para
        // que borrar la requisición borre sus líneas. La back-field
        // _lineas se accede vía HasField/UsePropertyAccessMode.
        builder.HasMany(r => r.Lineas)
            .WithOne()
            .HasForeignKey(l => l.RequisicionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Requisicion.Lineas))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Autorizaciones (F2-PR3): mismo patrón que líneas. CASCADE para
        // proteger integridad (no aplica en flujo normal — el header es
        // soft-delete — pero protege contra DROP TABLE accidental).
        builder.HasMany(r => r.Autorizaciones)
            .WithOne()
            .HasForeignKey(a => a.RequisicionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Requisicion.Autorizaciones))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
