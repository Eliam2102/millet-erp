using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    [Fact]
    public void EstadoCuentaEnvio_se_mapea_en_schema_del_modulo()
    {
        using var db = CrearDbContext();

        var entityType = db.Model.FindEntityType(typeof(Millet.CuentasPorCobrar.Domain.EstadoCuenta.EstadoCuentaEnvio));
        entityType.Should().NotBeNull("C7.8 registra cada envío del estado de cuenta");
        entityType!.GetSchema().Should().Be("cuentas_por_cobrar");
        entityType!.GetTableName().Should().Be("estado_cuenta_envio");
    }

    // C7.3 (86e3jvv6n): enums ampliados con 4 (PendienteAutorizacion /
    // Cobertura) y columnas nuevas, con sus constraints e índices.
    [Fact]
    public void DecisionLiberacion_constraints_e_indices_de_cobertura_C7_3()
    {
        using var db = new CuentasPorCobrarDbContext(
            new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
                .UseNpgsql("Host=invalid;Database=cxc_test")
                .UseSnakeCaseNamingConvention()
                .Options,
            new FakeEmpresaContext());
        var modelo = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var entity = modelo.FindEntityType(typeof(Millet.CuentasPorCobrar.Domain.Liberacion.DecisionLiberacion))!;

        var checks = entity.GetCheckConstraints().ToDictionary(c => c.ModelName, c => c.Sql);
        checks["ck_decision_liberacion_resultado"].Should().Be("resultado IN (1, 2, 3, 4)");
        checks["ck_decision_liberacion_regla"].Should().Be("regla_aplicada IN (1, 2, 3, 4)");
        checks["ck_decision_liberacion_override_consistente"].Should()
            .Be("(resultado = 3 AND override_id IS NOT NULL) OR (resultado != 3 AND override_id IS NULL)");
        checks["ck_decision_liberacion_pendiente_cobertura"].Should().Contain("porcentaje_cobertura >= 50");
        checks.Should().ContainKey("ck_decision_liberacion_cobertura_rango");
        // Autorizar (1/2/3 según el crédito recalculado) o rechazar (2).
        checks["ck_decision_liberacion_origen_resolucion"].Should()
            .Be("decision_origen_id IS NULL OR resultado IN (1, 2, 3)");
        checks.Should().NotContainKey("ck_decision_liberacion_origen_override");

        var motivo = entity.FindProperty("Motivo")!;
        motivo.IsNullable.Should().BeTrue();
        motivo.GetMaxLength().Should().Be(254);
        motivo.GetColumnName().Should().Be("motivo");

        var porcentaje = entity.FindProperty("PorcentajeCobertura")!;
        porcentaje.IsNullable.Should().BeTrue();
        porcentaje.GetPrecision().Should().Be(5);
        porcentaje.GetScale().Should().Be(2);
        porcentaje.GetColumnName().Should().Be("porcentaje_cobertura");
        entity.FindProperty("DecisionOrigenId")!.GetColumnName().Should().Be("decision_origen_id");

        var origen = entity.GetIndexes().Single(i => i.GetDatabaseName() == "ux_decision_liberacion_origen");
        origen.IsUnique.Should().BeTrue();
        origen.GetFilter().Should().Be("decision_origen_id IS NOT NULL");
        entity.GetIndexes().Should().Contain(i => i.GetDatabaseName() == "ix_decision_liberacion_resultado_fecha");
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
