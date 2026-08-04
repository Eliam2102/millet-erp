using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Cfdi;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence.Configurations;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Integraciones.Fiscal.Infrastructure.Persistence;

/// <summary>
/// DbContext del módulo Integraciones.Fiscal (PR-2 foundation). Schema:
/// <c>integraciones_fiscal</c>. Hereda de <see cref="BaseDbContext"/>
/// que aplica los interceptors transversales (Metadata, EmpresaContext,
/// Audit, Outbox).
///
/// <para>
/// Cross-schema FKs lógicas (no físicas — convención del monolito):
/// <c>empresa_id</c> referencia a <c>compartido.empresa(id)</c> pero
/// no se declara como FK física (preserva independencia del aggregate).
/// </para>
///
/// <para>
/// Outbox: <c>integraciones_fiscal.integration_events_outbox</c>.
/// PR-4 publica <c>IntegracionesFiscalConfiguracionActualizadaEvent</c>
/// para auditoría cuando el admin guarda; PR-5/PR-6 publican eventos
/// de descarga/refresh si surgen consumidores cross-módulo.
/// </para>
/// </summary>
public sealed class IntegracionesFiscalDbContext : BaseDbContext
{
    public IntegracionesFiscalDbContext(
        DbContextOptions<IntegracionesFiscalDbContext> options,
        ICurrentEmpresaContext empresaContext) : base(options, empresaContext)
    {
    }

    public DbSet<ConfiguracionPac> ConfiguracionesPac => Set<ConfiguracionPac>();
    public DbSet<RfcReceptor> RfcsReceptores => Set<RfcReceptor>();
    public DbSet<DownloadRuleExterna> DownloadRulesExternas => Set<DownloadRuleExterna>();
    public DbSet<SolicitudDescarga> SolicitudesDescarga => Set<SolicitudDescarga>();

    /// <summary>
    /// Archivos crudos de CFDI custodiados por el módulo (F2-PR1 Facturación):
    /// los CFDIs <b>emitidos</b> por Facturación. CxP custodia sus recibidos por
    /// su cuenta — no se unifican (alcance "Separados").
    /// </summary>
    public DbSet<CfdiArchivo> CfdisArchivo => Set<CfdiArchivo>();

    /// <summary>
    /// Outbox de eventos de integración del módulo. Lo escribe
    /// <c>OutboxSaveChangesInterceptor</c>; lo lee
    /// <c>OutboxPublisherWorker&lt;IntegracionesFiscalDbContext&gt;</c>
    /// para publicar al topic <c>integraciones-fiscal-events</c>.
    /// </summary>
    public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("integraciones_fiscal");
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ConfiguracionPacConfiguration());
        modelBuilder.ApplyConfiguration(new RfcReceptorConfiguration());
        modelBuilder.ApplyConfiguration(new DownloadRuleExternaConfiguration());
        modelBuilder.ApplyConfiguration(new SolicitudDescargaConfiguration());
        modelBuilder.ApplyConfiguration(new CfdiArchivoConfiguration());
        modelBuilder.ApplyConfiguration(new IntegrationEventOutboxEntryConfiguration());
    }
}
