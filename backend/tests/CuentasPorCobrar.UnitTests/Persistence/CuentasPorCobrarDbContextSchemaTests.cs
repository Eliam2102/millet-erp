using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.CuentasPorCobrar.UnitTests.Persistence;

/// <summary>
/// Asegura que el <see cref="CuentasPorCobrarDbContext"/> declara el
/// schema correcto y mapea la outbox table en él (CXC-PR1/PR2, mismo
/// patrón que CxP). Si alguien renombra accidentalmente el schema, este
/// test falla en CI antes del merge.
/// </summary>
public sealed class CuentasPorCobrarDbContextSchemaTests
{
    [Fact]
    public void Schema_default_es_cuentas_por_cobrar()
    {
        CuentasPorCobrarDbContext.SchemaName.Should().Be("cuentas_por_cobrar");
    }

    [Fact]
    public void OutboxEntries_se_mapea_en_schema_del_modulo()
    {
        using var db = CrearDbContext();

        var entityType = db.Model.FindEntityType(typeof(IntegrationEventOutboxEntry));
        entityType.Should().NotBeNull("el DbContext debe registrar la outbox del módulo");
        entityType!.GetSchema().Should().Be("cuentas_por_cobrar");
        entityType!.GetTableName().Should().Be("integration_events_outbox");
    }

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_test_{Guid.NewGuid()}")
            .Options;
        var empresaContext = new FakeEmpresaContext();
        return new CuentasPorCobrarDbContext(options, empresaContext);
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
