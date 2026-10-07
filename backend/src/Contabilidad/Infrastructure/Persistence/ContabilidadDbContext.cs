using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Contabilidad. Schema: <c>contabilidad</c> (ADR-0030).
/// Sin outbox en v1: no hay consumidor de eventos del catálogo todavía.
/// </summary>
// PLATFORM-TODO(<OutboxContabilidad>): agregar integration_events_outbox (ADR-0009) cuando Contabilidad emita eventos.
public sealed class ContabilidadDbContext : BaseDbContext
{
    public const string SchemaName = "contabilidad";

    public ContabilidadDbContext(
        DbContextOptions<ContabilidadDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext) { }

    public DbSet<CuentaContable> Cuentas => Set<CuentaContable>();
    public DbSet<CuentaContableOrigen> Origenes => Set<CuentaContableOrigen>();
    public DbSet<ImportacionCatalogo> Importaciones => Set<ImportacionCatalogo>();
    public DbSet<CuentaContableUso> Usos => Set<CuentaContableUso>();

    // F1-CON-02: dimensiones contables.
    public DbSet<TipoDocumentoContable> TiposDocumento => Set<TipoDocumentoContable>();
    public DbSet<ReglaDimension> ReglasDimension => Set<ReglaDimension>();
    public DbSet<ReglaDimensionUso> ReglasDimensionUso => Set<ReglaDimensionUso>();
    public DbSet<UbicacionSucursal> UbicacionesSucursal => Set<UbicacionSucursal>();
    public DbSet<CentroCorporativo> CentrosCorporativos => Set<CentroCorporativo>();
    public DbSet<MovimientoDimensionPrueba> MovimientosPrueba => Set<MovimientoDimensionPrueba>();

    // F1-CON-03 (C1.1): periodos contables y su historial.
    public DbSet<PeriodoContable> Periodos => Set<PeriodoContable>();
    public DbSet<PeriodoContableEvento> PeriodosEventos => Set<PeriodoContableEvento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        base.OnModelCreating(modelBuilder);

        // ADR-0045: búsqueda insensible a acentos con translate() nativo (sin extensiones).
        modelBuilder
            .HasDbFunction(typeof(PostgresFunctions).GetMethod(
                nameof(PostgresFunctions.Translate), [typeof(string), typeof(string), typeof(string)])!)
            .HasName("translate")
            .HasSchema("pg_catalog");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContabilidadDbContext).Assembly);
    }
}
