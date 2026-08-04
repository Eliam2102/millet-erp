using Microsoft.EntityFrameworkCore;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.CentrosCosto.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Centros de Costo. Schema: <c>centros_costo</c>
/// (ADR-0030 — un esquema por módulo).
///
/// <para>
/// Modelo Dim (CECO-PR4, 01-diseno Rev 0.4): jerarquía Dim1 → Dim2 → Dim3
/// + grupos GrupoDim2/GrupoDim3, TODO tablas propias — separación total de
/// catálogos compartidos (cero relaciones fuera del esquema). Sin outbox:
/// el módulo no emite eventos de integración; cuando Contabilidad consuma
/// el catálogo se agregará la <c>integration_events_outbox</c> (ADR-0009).
/// El alcance usuario→máquinas (CECO-PR6) vive en <c>asignaciones</c> —
/// congelado en Dim3, 01-diseno §7.
/// </para>
/// </summary>
public sealed class CentrosCostoDbContext : BaseDbContext
{
    public const string SchemaName = "centros_costo";

    public CentrosCostoDbContext(
        DbContextOptions<CentrosCostoDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    /// <summary>Nivel 1 (UI-config "Dimensión 1"; la planta del negocio, ej. 101/CONKAL).</summary>
    public DbSet<Dim1> Dim1s => Set<Dim1>();

    /// <summary>Grupos de la Dimensión 2 (clasifican Dim2; no son nivel).</summary>
    public DbSet<GrupoDim2> GruposDim2 => Set<GrupoDim2>();

    /// <summary>Grupos de la Dimensión 3 (clasifican Dim3; no son nivel).</summary>
    public DbSet<GrupoDim3> GruposDim3 => Set<GrupoDim3>();

    /// <summary>Nivel 2 (UI-config "Dimensión 2").</summary>
    public DbSet<Dim2> Dim2s => Set<Dim2>();

    /// <summary>Nivel 3 — hoja (UI-config "Dimensión 3"; "Máquina" en documentos — lo único seleccionable).</summary>
    public DbSet<Dim3> Dim3s => Set<Dim3>();

    /// <summary>Alcance congelado usuario → Dim3 (§7). Filas de hecho: borrado físico.</summary>
    public DbSet<Asignacion> Asignaciones => Set<Asignacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new Dim1Configuration());
        modelBuilder.ApplyConfiguration(new GrupoDim2Configuration());
        modelBuilder.ApplyConfiguration(new GrupoDim3Configuration());
        modelBuilder.ApplyConfiguration(new Dim2Configuration());
        modelBuilder.ApplyConfiguration(new Dim3Configuration());
        modelBuilder.ApplyConfiguration(new AsignacionConfiguration());
    }
}
