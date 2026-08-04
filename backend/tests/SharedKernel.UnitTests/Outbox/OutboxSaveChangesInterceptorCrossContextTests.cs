using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

namespace Millet.SharedKernel.UnitTests.Outbox;

/// <summary>
/// Regresión de P9-H7: el <see cref="OutboxSaveChangesInterceptor"/> es un
/// único interceptor scoped compartido por varios DbContext que comparten el
/// mismo <see cref="IIntegrationEventBuffer"/>. Antes, el primer SaveChanges
/// del scope drenaba TODO el buffer y escribía las filas en SU propio schema
/// — por lo que el SaveChanges del timbrado de la NC de amortización (schema
/// <c>integraciones_fiscal</c>) se robaba el evento de la NC anterior, que
/// pertenecía a <c>facturacion</c>. Resultado: NC perdida en un schema sin
/// worker publisher y saldo fantasma en la cartera de CxC.
///
/// <para>
/// Estos tests montan dos DbContext reales (uno con schema
/// <c>facturacion</c>, otro <c>integraciones_fiscal</c>) sobre EF InMemory,
/// compartiendo un buffer y un interceptor, y reproducen el orden exacto de
/// <c>AmortizarAnticiposAsync</c> con dos anticipos. Son dos <b>tipos</b> de
/// contexto distintos a propósito: EF cachea el modelo por tipo de contexto,
/// así cada uno resuelve su propio <c>HasDefaultSchema</c>.
/// </para>
/// </summary>
public sealed class OutboxSaveChangesInterceptorCrossContextTests
{
    private static FacturacionLikeContext NewFacturacion(OutboxSaveChangesInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<FacturacionLikeContext>()
                .UseInMemoryDatabase($"facturacion-{Guid.NewGuid()}")
                .AddInterceptors(interceptor)
                .Options);

    private static FiscalLikeContext NewFiscal(OutboxSaveChangesInterceptor interceptor) =>
        new(new DbContextOptionsBuilder<FiscalLikeContext>()
                .UseInMemoryDatabase($"fiscal-{Guid.NewGuid()}")
                .AddInterceptors(interceptor)
                .Options);

    private static TestEvent NcTimbrada() =>
        new("facturacion.nota-credito.timbrada.v1", Guid.CreateVersion7(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task DosAnticipos_AmbasNc_QuedanEnFacturacion_CeroEnFiscal()
    {
        var buffer = new InMemoryIntegrationEventBuffer();
        var interceptor = new OutboxSaveChangesInterceptor(buffer, NullLogger<OutboxSaveChangesInterceptor>.Instance);
        var publisher = new OutboxIntegrationEventPublisher(buffer, NullLogger<OutboxIntegrationEventPublisher>.Instance);

        await using var facturacion = NewFacturacion(interceptor);
        await using var fiscal = NewFiscal(interceptor);

        // Iteración 1: timbrado NC1 → SaveChanges del contexto fiscal (repo CFDI),
        // luego PublishAsync del evento de la NC1.
        await fiscal.SaveChangesAsync();
        await publisher.PublishAsync(NcTimbrada(), CancellationToken.None);

        // Iteración 2: timbrado NC2 → SaveChanges del contexto fiscal (aquí el
        // bug se robaba la NC1), luego PublishAsync del evento de la NC2.
        await fiscal.SaveChangesAsync();
        await publisher.PublishAsync(NcTimbrada(), CancellationToken.None);

        // SaveChanges final del handler: el contexto de Facturación.
        await facturacion.SaveChangesAsync();

        var enFacturacion = await facturacion.OutboxEntries.AsNoTracking().CountAsync();
        var enFiscal = await fiscal.OutboxEntries.AsNoTracking().CountAsync();

        enFacturacion.Should().Be(2, "ambas NC de amortización pertenecen a facturacion");
        enFiscal.Should().Be(0, "ningún evento de facturacion debe caer en integraciones_fiscal");
    }

    [Fact]
    public async Task EventoFiscalReal_SiCaeEnFiscal_NoEnFacturacion()
    {
        var buffer = new InMemoryIntegrationEventBuffer();
        var interceptor = new OutboxSaveChangesInterceptor(buffer, NullLogger<OutboxSaveChangesInterceptor>.Instance);
        var publisher = new OutboxIntegrationEventPublisher(buffer, NullLogger<OutboxIntegrationEventPublisher>.Instance);

        await using var facturacion = NewFacturacion(interceptor);
        await using var fiscal = NewFiscal(interceptor);

        await publisher.PublishAsync(
            new TestEvent("integraciones.fiscal.configuracion-actualizada.v1", Guid.CreateVersion7(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        // Aunque facturacion guarde primero, NO se lleva el evento fiscal.
        await facturacion.SaveChangesAsync();
        await fiscal.SaveChangesAsync();

        (await facturacion.OutboxEntries.AsNoTracking().CountAsync()).Should().Be(0);
        (await fiscal.OutboxEntries.AsNoTracking().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EventoSinRuta_UsaFallbackLegacy_PrimerSaveChanges()
    {
        var buffer = new InMemoryIntegrationEventBuffer();
        var interceptor = new OutboxSaveChangesInterceptor(buffer, NullLogger<OutboxSaveChangesInterceptor>.Instance);
        var publisher = new OutboxIntegrationEventPublisher(buffer, NullLogger<OutboxIntegrationEventPublisher>.Instance);

        await using var facturacion = NewFacturacion(interceptor);
        await using var fiscal = NewFiscal(interceptor);

        // admin.* no tiene outbox propio → fallback legacy: lo absorbe el
        // primer SaveChanges (fiscal aquí).
        await publisher.PublishAsync(
            new TestEvent("admin.empresa.creada.v1", Guid.CreateVersion7(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        await fiscal.SaveChangesAsync();
        await facturacion.SaveChangesAsync();

        (await fiscal.OutboxEntries.AsNoTracking().CountAsync()).Should().Be(1);
        (await facturacion.OutboxEntries.AsNoTracking().CountAsync()).Should().Be(0);
    }

    private sealed record TestEvent(string EventType, Guid EmpresaId, DateTimeOffset OcurridoEn)
        : IntegrationEvent(EventType, EmpresaId, OcurridoEn);

    // Dos tipos de contexto distintos → dos claves de caché de modelo EF
    // distintas → cada uno resuelve su propio HasDefaultSchema.
    private sealed class FacturacionLikeContext(DbContextOptions<FacturacionLikeContext> options)
        : OutboxOnlyContext(options)
    {
        protected override string Schema => "facturacion";
    }

    private sealed class FiscalLikeContext(DbContextOptions<FiscalLikeContext> options)
        : OutboxOnlyContext(options)
    {
        protected override string Schema => "integraciones_fiscal";
    }

    /// <summary>
    /// Base mínima que mapea el outbox a <see cref="Schema"/>, igual que los
    /// DbContext de módulo (HasDefaultSchema + ToTable). Es lo único que el
    /// interceptor necesita para resolver su schema desde el modelo EF.
    /// </summary>
    private abstract class OutboxOnlyContext(DbContextOptions options) : DbContext(options)
    {
        protected abstract string Schema { get; }

        public DbSet<IntegrationEventOutboxEntry> OutboxEntries => Set<IntegrationEventOutboxEntry>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(Schema);
            modelBuilder.Entity<IntegrationEventOutboxEntry>(e =>
            {
                e.ToTable("integration_events_outbox");
                e.HasKey(x => x.Id);
                e.Property(x => x.EventType);
                e.Property(x => x.Payload);
                e.Property(x => x.OccurredAt);
                e.Property(x => x.IntegrationEmpresaId);
            });
        }
    }
}
