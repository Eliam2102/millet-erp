using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure;
using Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

namespace Millet.SharedKernel.UnitTests.Persistence;

public sealed class AuditSnapshotTests
{
    public enum EstadoPrueba
    {
        Borrador = 0,
        Activo = 1,
        Inactivo = 2
    }

    private sealed class FakeAuditableEntity : BaseEntity, IAuditable
    {
        public string Clave { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public EstadoPrueba Estado { get; set; } = EstadoPrueba.Borrador;
        public bool EsActivo { get; set; } = true;
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Clabe { get; set; }
        public string? ApiKey { get; set; }

        public FakeAuditableEntity() : base(Guid.CreateVersion7()) { }

        public void SetCamposTecnicosParaTest(int version, DateTimeOffset updatedAt)
        {
            Version = version;
            UpdatedAt = updatedAt;
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
    {
        public DbSet<FakeAuditableEntity> Entidades => Set<FakeAuditableEntity>();
        public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class FakeUserContext(Guid? userId, string? userName, string? email = null) : ICurrentUserContext
    {
        public Guid? UserId => userId;
        public string? UserName => userName;
        public string? Email => email;
    }

    private sealed class FakeEmpresaContext(bool bypassed) : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => bypassed;
        public IDisposable Bypass() => throw new NotSupportedException();
    }

    private static TestDbContext NewContext(
        ICurrentUserContext userContext,
        ICurrentEmpresaContext empresaContext,
        IAuditOriginContext originContext)
    {
        var audit = new AuditSaveChangesInterceptor(
            new FixedClock(DateTimeOffset.UtcNow),
            userContext,
            empresaContext,
            originContext,
            new AuditCorrelationContext());

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"audit-snapshot-{Guid.NewGuid()}")
            .AddInterceptors(audit)
            .Options;

        return new TestDbContext(options);
    }

    [Fact]
    public async Task Crear_ConUsuarioAutenticado_PopulaActorUsuario_Etiqueta_Resumen_Y_SnapshotTexto()
    {
        var user = new FakeUserContext(Guid.NewGuid(), "Juana Pérez", "juana@millet.mx");
        var empresa = new FakeEmpresaContext(bypassed: false);
        var origin = new AuditOriginContext();

        await using var db = NewContext(user, empresa, origin);
        var entidad = new FakeAuditableEntity
        {
            Clave = "EMP-0012",
            Nombre = "Juana Pérez",
            Estado = EstadoPrueba.Activo,
            EsActivo = true,
            Password = "supersecreto"
        };

        db.Entidades.Add(entidad);
        await db.SaveChangesAsync();

        var log = await db.AuditLog.SingleAsync();
        log.ActorTipo.Should().Be("usuario");
        log.ActorNombre.Should().Be("Juana Pérez");
        log.ActorEmail.Should().Be("juana@millet.mx");
        log.EntidadEtiqueta.Should().Be("EMP-0012 · Juana Pérez");
        log.Resumen.Should().Be("Creó FakeAuditableEntity EMP-0012 · Juana Pérez");

        log.Cambios.Should().NotContain("supersecreto");
        log.Cambios.Should().NotContain("Password");

        using var doc = JsonDocument.Parse(log.Cambios);
        var snapshot = doc.RootElement.GetProperty("snapshot");
        snapshot.GetProperty("Clave").GetString().Should().Be("EMP-0012");
        snapshot.GetProperty("Estado").GetInt32().Should().Be(1);

        var snapshotTexto = doc.RootElement.GetProperty("snapshotTexto");
        snapshotTexto.GetProperty("Estado").GetString().Should().Be("Activo");
        snapshotTexto.GetProperty("EsActivo").GetString().Should().Be("Sí");
    }

    [Fact]
    public async Task Crear_ConProcesoEnBackground_PopulaActorProceso()
    {
        var user = new FakeUserContext(null, null);
        var empresa = new FakeEmpresaContext(bypassed: true);
        var origin = new AuditOriginContext();

        await using var db = NewContext(user, empresa, origin);
        using (origin.SetOrigin("SyncEmpleadosWorker"))
        {
            db.Entidades.Add(new FakeAuditableEntity { Clave = "EMP-0999" });
            await db.SaveChangesAsync();
        }

        var log = await db.AuditLog.SingleAsync();
        log.ActorTipo.Should().Be("proceso");
        log.ActorNombre.Should().Be("Proceso: SyncEmpleadosWorker");
        log.ActorEmail.Should().BeNull();
        log.EntidadEtiqueta.Should().Be("EMP-0999");
    }

    [Fact]
    public async Task Crear_SinUsuarioNiProceso_PopulaActorSistema()
    {
        var user = new FakeUserContext(null, null);
        var empresa = new FakeEmpresaContext(bypassed: false);
        var origin = new AuditOriginContext();

        await using var db = NewContext(user, empresa, origin);
        db.Entidades.Add(new FakeAuditableEntity { Nombre = "Solo Nombre" });
        await db.SaveChangesAsync();

        var log = await db.AuditLog.SingleAsync();
        log.ActorTipo.Should().Be("sistema");
        log.ActorNombre.Should().Be("Sistema");
        log.ActorEmail.Should().BeNull();
        log.EntidadEtiqueta.Should().Be("Solo Nombre");
    }

    [Fact]
    public async Task Modificar_CambiosDeNegocio_PopulaDiffLegible_SinRomperValoresCrudos()
    {
        var user = new FakeUserContext(Guid.NewGuid(), "Admin", "admin@millet.mx");
        var empresa = new FakeEmpresaContext(bypassed: false);
        var origin = new AuditOriginContext();

        await using var db = NewContext(user, empresa, origin);
        var entidad = new FakeAuditableEntity
        {
            Clave = "EMP-001",
            Nombre = "Carlos",
            Estado = EstadoPrueba.Borrador,
            EsActivo = true
        };
        db.Entidades.Add(entidad);
        await db.SaveChangesAsync();

        // Modificar estado y activo
        entidad.Estado = EstadoPrueba.Inactivo;
        entidad.EsActivo = false;
        await db.SaveChangesAsync();

        var logs = await db.AuditLog.OrderBy(l => l.Timestamp).ToListAsync();
        logs.Should().HaveCount(2);

        var updateLog = logs[1];
        updateLog.Operacion.Should().Be("actualizar");
        updateLog.Resumen.Should().Contain("Modificó FakeAuditableEntity EMP-001 · Carlos");
        updateLog.Resumen.Should().Contain("Estado: Borrador → Inactivo");
        updateLog.Resumen.Should().Contain("EsActivo: Sí → No");

        using var doc = JsonDocument.Parse(updateLog.Cambios);
        var diff = doc.RootElement.GetProperty("diff");

        // Estado conserva raw antes/despues (fundamental para HistoricoTipoMapper de Compras)
        var estadoDiff = diff.GetProperty("Estado");
        estadoDiff.GetProperty("antes").GetInt32().Should().Be(0);
        estadoDiff.GetProperty("despues").GetInt32().Should().Be(2);
        estadoDiff.GetProperty("antesTexto").GetString().Should().Be("Borrador");
        estadoDiff.GetProperty("despuesTexto").GetString().Should().Be("Inactivo");

        // EsActivo conserva bool y suma texto
        var activoDiff = diff.GetProperty("EsActivo");
        activoDiff.GetProperty("antes").GetBoolean().Should().BeTrue();
        activoDiff.GetProperty("despues").GetBoolean().Should().BeFalse();
        activoDiff.GetProperty("antesTexto").GetString().Should().Be("Sí");
        activoDiff.GetProperty("despuesTexto").GetString().Should().Be("No");
    }

    [Fact]
    public async Task Modificar_SoloCamposTecnicos_NoEscribeFilaDeAuditoria()
    {
        var user = new FakeUserContext(Guid.NewGuid(), "Admin");
        var empresa = new FakeEmpresaContext(bypassed: false);
        var origin = new AuditOriginContext();

        await using var db = NewContext(user, empresa, origin);
        var entidad = new FakeAuditableEntity { Clave = "EMP-001" };
        db.Entidades.Add(entidad);
        await db.SaveChangesAsync();

        db.AuditLog.Count().Should().Be(1);

        // Modificar unicamente campo tecnico Version y UpdatedAt
        entidad.SetCamposTecnicosParaTest(2, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        // No debe agregarse ninguna fila en audit_log
        db.AuditLog.Count().Should().Be(1, "modificar solo campos tecnicos no genera evento de auditoria");
    }

    [Fact]
    public async Task CrearYModificar_DatosSensibles_RegistraAccionSinExponerValores()
    {
        var user = new FakeUserContext(Guid.NewGuid(), "Admin");
        await using var db = NewContext(user, new FakeEmpresaContext(false), new AuditOriginContext());
        var entidad = new FakeAuditableEntity
        {
            Clave = "PROV-1",
            Clabe = "012345678901234567",
            ApiKey = "secreto-inicial"
        };

        db.Entidades.Add(entidad);
        await db.SaveChangesAsync();

        entidad.Clabe = "987654321098765432";
        entidad.ApiKey = "secreto-nuevo";
        await db.SaveChangesAsync();

        var logs = await db.AuditLog.OrderBy(l => l.Timestamp).ToListAsync();
        logs.Should().HaveCount(2);
        foreach (var log in logs)
        {
            log.Cambios.Should().NotContain("012345678901234567").And.NotContain("987654321098765432");
            log.Cambios.Should().NotContain("secreto-inicial").And.NotContain("secreto-nuevo");
            log.Resumen.Should().NotContain("012345678901234567").And.NotContain("987654321098765432");
            log.Resumen.Should().NotContain("secreto-inicial").And.NotContain("secreto-nuevo");
        }

        using var created = JsonDocument.Parse(logs[0].Cambios);
        created.RootElement.GetProperty("snapshot").GetProperty("Clabe").GetString()
            .Should().Be("[PROTEGIDO]");
        using var updated = JsonDocument.Parse(logs[1].Cambios);
        updated.RootElement.GetProperty("diff").GetProperty("Clabe").GetProperty("despues").GetString()
            .Should().Be("[PROTEGIDO]");
    }

    [Fact]
    public async Task ValoresLargos_NoExcedenLasColumnasDeAuditoria()
    {
        var user = new FakeUserContext(Guid.NewGuid(), new string('A', 150), new string('b', 250) + "@m.mx");
        await using var db = NewContext(user, new FakeEmpresaContext(false), new AuditOriginContext());
        var entidad = new FakeAuditableEntity { Clave = "LARGA", Nombre = new string('N', 400) };

        db.Entidades.Add(entidad);
        await db.SaveChangesAsync();
        entidad.Nombre = new string('C', 1000);
        await db.SaveChangesAsync();

        var logs = await db.AuditLog.ToListAsync();
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(log => log.ActorNombre.Length <= 128 &&
            log.ActorEmail != null && log.ActorEmail.Length <= 256 &&
            log.EntidadEtiqueta.Length <= 256 && log.Resumen.Length <= 512);
    }
}
