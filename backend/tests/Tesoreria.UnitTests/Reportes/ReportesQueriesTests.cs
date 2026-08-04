using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.Tesoreria.Application.Reportes;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Reportes;

/// <summary>
/// Tests de los reportes ADR-0036 (TES-PR10): contrato JSON completo y
/// saldo acumulado del auxiliar.
/// </summary>
public sealed class ReportesQueriesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FlujoEfectivo_devuelve_contrato_con_totales()
    {
        using var db = CrearDbContext();
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");
        db.CuentasBancarias.Add(cuenta);
        db.MovimientosBancarios.Add(Ingreso(cuenta, 10_000m, new DateOnly(2026, 7, 10)));
        db.MovimientosBancarios.Add(Ingreso(cuenta, 5_000m, new DateOnly(2026, 7, 12)));
        await db.SaveChangesAsync();

        var handler = new FlujoEfectivoReporteHandler(db, new FakeClock(Ahora));
        var reporte = await handler.Handle(
            new FlujoEfectivoReporteQuery(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            CancellationToken.None);

        reporte.Titulo.Should().Be("Flujo de efectivo");
        reporte.GeneradoEn.Should().Be(Ahora);
        reporte.Columnas.Should().HaveCount(6);
        reporte.Filas.Should().HaveCount(1); // (Sin concepto) MXN agregado
        reporte.Filas[0]["ingresos"].Should().Be(15_000m);
        reporte.Totales!["neto"].Should().Be(15_000m);
        reporte.FiltrosAplicados.Should().Contain(f => f.Label == "Del" && f.Valor == "2026-07-01");
    }

    [Fact]
    public async Task AuxiliarBancos_acumula_saldo_y_arrastra_previo()
    {
        using var db = CrearDbContext();
        var cuenta = new CuentaBancaria(Guid.NewGuid(), "BBVA", "12345678", null, "MXN");
        db.CuentasBancarias.Add(cuenta);
        // Previo al período: arrastre.
        db.MovimientosBancarios.Add(Ingreso(cuenta, 1_000m, new DateOnly(2026, 6, 30)));
        // Dentro del período.
        db.MovimientosBancarios.Add(Ingreso(cuenta, 500m, new DateOnly(2026, 7, 5)));
        await db.SaveChangesAsync();

        var handler = new AuxiliarBancosReporteHandler(db, new FakeClock(Ahora));
        var reporte = await handler.Handle(
            new AuxiliarBancosReporteQuery(cuenta.Id, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            CancellationToken.None);

        reporte.Filas.Should().HaveCount(1);
        reporte.Filas[0]["saldo"].Should().Be(1_500m); // 1000 previo + 500
        reporte.Totales!["saldo"].Should().Be(1_500m);
        // PII: el título lleva la cuenta enmascarada.
        reporte.Titulo.Should().Contain("****5678").And.NotContain("12345678");
    }

    // ------------------------------------------------------------ helpers

    private static MovimientoBancario Ingreso(CuentaBancaria cuenta, decimal monto, DateOnly fecha) =>
        MovimientoBancario.RegistrarIngreso(
            cuenta.EmpresaId, cuenta, monto, fecha, null, null, null, null, Guid.NewGuid(), Ahora);

    private static TesoreriaDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(databaseName: $"tesoreria_rep_{Guid.NewGuid()}")
            .Options;
        return new TesoreriaDbContext(options, new FakeEmpresaContext());
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

    private sealed class FakeClock(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }
}
