using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

namespace Millet.SharedKernel.UnitTests.Persistence;

/// <summary>
/// Prueba end-to-end (sobre EF InMemory) del mecanismo de atribución de
/// origen de F1-ADM-03: cuando un worker en background declara su origen
/// con <see cref="IAuditOriginContext.SetOrigin"/> dentro de un bypass de
/// empresa, <see cref="MetadataSaveChangesInterceptor"/> debe usarlo para
/// <c>CreatedBy</c>/<c>UpdatedBy</c> en vez de <c>"system"</c>, y
/// <see cref="AuditSaveChangesInterceptor"/> debe poblar
/// <c>AuditLogEntry.Metadatos</c> con <c>{"origen": "..."}</c>.
/// </summary>
public sealed class AuditOriginAttributionTests
{
    private sealed class FakeEntity : BaseEntity, IAuditable
    {
        public string Nombre { get; set; } = string.Empty;
        public FakeEntity() : base(Guid.CreateVersion7()) { }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<FakeEntity> Entidades => Set<FakeEntity>();
        public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeUserContext(Guid? userId, string? userName) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? UserName => userName;
    }

    private sealed class FakeEmpresaContext(bool bypassed) : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => bypassed;
        public IDisposable Bypass() => throw new NotSupportedException("No usado en este test.");
    }

    private static TestDbContext NewContext(
        ICurrentUserContext userContext, ICurrentEmpresaContext empresaContext, IAuditOriginContext originContext)
    {
        var metadata = new MetadataSaveChangesInterceptor(new FixedClock(DateTimeOffset.UtcNow), userContext, originContext);
        var audit = new AuditSaveChangesInterceptor(new FixedClock(DateTimeOffset.UtcNow), userContext, empresaContext, originContext, new AuditCorrelationContext());

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"audit-origin-{Guid.NewGuid()}")
            .AddInterceptors(metadata, audit)
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task RequestNormal_ConUsuario_NoPopulaMetadatos()
    {
        var userContext = new FakeUserContext(Guid.NewGuid(), "maria@millet.mx");
        var empresaContext = new FakeEmpresaContext(bypassed: false);
        var originContext = new AuditOriginContext(); // Origin null: nadie llamó SetOrigin.

        await using var db = NewContext(userContext, empresaContext, originContext);
        db.Entidades.Add(new FakeEntity { Nombre = "Prueba" });
        await db.SaveChangesAsync();

        var entidad = await db.Entidades.SingleAsync();
        entidad.CreatedBy.Should().Be("maria@millet.mx");

        var logRow = await db.AuditLog.SingleAsync();
        logRow.Metadatos.Should().BeNull();
        logRow.UsuarioId.Should().Be(userContext.UserId);
    }

    [Fact]
    public async Task WorkerEnBackground_ConSetOrigin_PopulaCreatedByYMetadatos()
    {
        var userContext = new FakeUserContext(userId: null, userName: null); // sin HttpContext.
        var empresaContext = new FakeEmpresaContext(bypassed: true); // worker bajo Bypass().
        var originContext = new AuditOriginContext();

        await using var db = NewContext(userContext, empresaContext, originContext);

        using (originContext.SetOrigin(nameof(AuditOriginAttributionTests)))
        {
            db.Entidades.Add(new FakeEntity { Nombre = "Desde worker" });
            await db.SaveChangesAsync();
        }

        var entidad = await db.Entidades.SingleAsync();
        entidad.CreatedBy.Should().Be(nameof(AuditOriginAttributionTests),
            "el worker declaró su origen: CreatedBy no debe caer al literal genérico \"system\"");

        var logRow = await db.AuditLog.SingleAsync();
        logRow.UsuarioId.Should().BeNull();
        logRow.Metadatos.Should().Be($"{{\"origen\":\"{nameof(AuditOriginAttributionTests)}\"}}");
    }

    [Fact]
    public async Task WorkerEnBackground_SinSetOrigin_CaeAlLiteralSystem()
    {
        var userContext = new FakeUserContext(userId: null, userName: null);
        var empresaContext = new FakeEmpresaContext(bypassed: true);
        var originContext = new AuditOriginContext(); // Nunca se llamó SetOrigin (ej. Migrations/Seed genéricos).

        await using var db = NewContext(userContext, empresaContext, originContext);
        db.Entidades.Add(new FakeEntity { Nombre = "Sin origen declarado" });
        await db.SaveChangesAsync();

        var entidad = await db.Entidades.SingleAsync();
        entidad.CreatedBy.Should().Be("system");

        var logRow = await db.AuditLog.SingleAsync();
        logRow.Metadatos.Should().BeNull();
    }

    [Fact]
    public void SetOrigin_RestauraElValorAnteriorAlSalirDelScope()
    {
        var originContext = new AuditOriginContext();
        originContext.Origin.Should().BeNull();

        using (originContext.SetOrigin("WorkerExterno"))
        {
            originContext.Origin.Should().Be("WorkerExterno");

            using (originContext.SetOrigin("WorkerAnidado"))
            {
                originContext.Origin.Should().Be("WorkerAnidado");
            }

            originContext.Origin.Should().Be("WorkerExterno");
        }

        originContext.Origin.Should().BeNull();
    }

    [Fact]
    public async Task CorrelacionComun_AgrupaVariosSaveChanges_YSinElla_CadaUnoTieneLaSuya()
    {
        var userContext = new FakeUserContext(Guid.NewGuid(), "maria@millet.mx");
        var empresaContext = new FakeEmpresaContext(bypassed: false);
        await using var db = NewContext(userContext, empresaContext, new AuditOriginContext());
        var correlacion = Guid.CreateVersion7();

        using (new AuditCorrelationContext().Begin(correlacion))
        {
            db.Entidades.Add(new FakeEntity { Nombre = "Primero" });
            await db.SaveChangesAsync();
            db.Entidades.Add(new FakeEntity { Nombre = "Segundo" });
            await db.SaveChangesAsync();
        }
        db.Entidades.Add(new FakeEntity { Nombre = "Fuera del scope" });
        await db.SaveChangesAsync();

        var correlaciones = await db.AuditLog.Select(a => a.CorrelationId).ToListAsync();
        correlaciones.Count(c => c == correlacion).Should().Be(2);
        correlaciones.Distinct().Should().HaveCount(2, "la escritura fuera del scope genera su propia correlación");
    }

    [Fact]
    public void BeginCorrelacion_RestauraElValorAnteriorAlSalirDelScope()
    {
        var context = new AuditCorrelationContext();
        var externa = Guid.CreateVersion7();
        var anidada = Guid.CreateVersion7();

        using (context.Begin(externa))
        {
            using (context.Begin(anidada))
            {
                context.CorrelationId.Should().Be(anidada);
            }

            context.CorrelationId.Should().Be(externa);
        }

        context.CorrelationId.Should().BeNull();
    }
}
