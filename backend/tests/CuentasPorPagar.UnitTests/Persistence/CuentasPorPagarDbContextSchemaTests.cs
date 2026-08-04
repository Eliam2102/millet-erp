using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.CuentasPorPagar.UnitTests.Persistence;

/// <summary>
/// Asegura que el <see cref="CuentasPorPagarDbContext"/> declara el
/// schema correcto y mapea la outbox table en él (F0-PR1). Si alguien
/// renombra accidentalmente el schema, este test falla en CI antes del
/// merge.
/// </summary>
public sealed class CuentasPorPagarDbContextSchemaTests
{
    [Fact]
    public void Schema_default_es_cuentas_por_pagar()
    {
        CuentasPorPagarDbContext.SchemaName.Should().Be("cuentas_por_pagar");
    }

    [Fact]
    public void OutboxEntries_se_mapea_en_schema_del_modulo()
    {
        using var db = CrearDbContext();

        var entityType = db.Model.FindEntityType(typeof(IntegrationEventOutboxEntry));
        entityType.Should().NotBeNull("el DbContext debe registrar la outbox del módulo");
        entityType!.GetSchema().Should().Be("cuentas_por_pagar");
        entityType!.GetTableName().Should().Be("integration_events_outbox");
    }

    private static CuentasPorPagarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxp_test_{Guid.NewGuid()}")
            .Options;
        var empresaContext = new FakeEmpresaContext();
        return new CuentasPorPagarDbContext(options, empresaContext);
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
