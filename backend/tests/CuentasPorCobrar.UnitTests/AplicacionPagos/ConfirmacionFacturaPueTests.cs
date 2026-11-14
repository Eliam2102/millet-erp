using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.EventListeners;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.AplicacionPagos;

/// <summary>
/// H-U49: una factura PUE no lleva REP. Al confirmar Tesorería el depósito, CxC aplica su pago a cartera (origen = propuesta) y,
/// si todas las facturas son PUE, la propuesta queda cerrada. Una PUE ya cobrada en caja no se abona dos veces. Datos ficticios.
/// </summary>
public sealed class ConfirmacionFacturaPueTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly Guid Proponente = Guid.NewGuid();
    private static readonly Guid Tesoreria = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 18, 0, 0, TimeSpan.Zero);

    private static CuentasPorCobrarDbContext Db() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(EmpresaId));

    private static FacturaCartera Factura(CuentasPorCobrarDbContext db, string folio, decimal total, string metodo)
    {
        var f = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), ClienteId, "AAA010101AAA", "Cliente Prueba",
            $"UUID-{folio}", folio, total, "MXN", metodo, Ahora.AddDays(-5), Ahora.AddDays(25));
        db.FacturasCartera.Add(f);
        return f;
    }

    private static PropuestaAplicacionPago Propuesta(CuentasPorCobrarDbContext db, params (FacturaCartera F, decimal Importe, int? Parcialidad)[] lineas)
    {
        var p = PropuestaAplicacionPago.Crear(EmpresaId, ClienteId, "DEP-1", lineas.Sum(l => l.Importe), "MXN", "REM-1",
            [.. lineas.Select(l => (l.F.Uuid, l.F.Id, l.Importe, l.Parcialidad))], 0m, propuestoPor: Proponente, ahora: Ahora);
        db.PropuestasAplicacionPago.Add(p);
        return p;
    }

    private static readonly Guid Movimiento = Guid.NewGuid();

    private static Task Confirmar(CuentasPorCobrarDbContext db, PropuestaAplicacionPago p) =>
        new ResolverPropuestaTesoreriaHandler(db, new FakeClock())
            .Handle(new(Guid.NewGuid(), EmpresaId, p.Id, Tesoreria, Ahora, Movimiento, null), default);

    [Fact]
    public async Task Confirmar_propuesta_PUE_salda_la_factura_y_cierra_la_propuesta()
    {
        using var db = Db();
        var pue = Factura(db, "FX-000001", 1100m, "PUE");
        var p = Propuesta(db, (pue, 1100m, null));
        await db.SaveChangesAsync();

        await Confirmar(db, p);

        pue.SaldoPendiente.Should().Be(0m);
        p.Estado.Should().Be(EstadoPropuestaAplicacion.Confirmada);
        p.ReppTimbrado.Should().BeTrue("una PUE no espera REP");
        var mov = await db.MovimientosCartera.SingleAsync();
        mov.OrigenComprobanteId.Should().Be(p.Id);
        mov.Importe.Should().Be(1100m);
    }

    [Fact]
    public async Task Confirmacion_repetida_no_abona_dos_veces()
    {
        using var db = Db();
        var pue = Factura(db, "FX-000002", 500m, "PUE");
        var ppd = Factura(db, "FX-000003", 800m, "PPD");
        var p = Propuesta(db, (pue, 500m, null), (ppd, 800m, 1));
        await db.SaveChangesAsync();

        await Confirmar(db, p);
        await Confirmar(db, p); // otro EventId, misma confirmación

        (await db.MovimientosCartera.CountAsync()).Should().Be(1);
        pue.SaldoPendiente.Should().Be(0m);
        ppd.SaldoPendiente.Should().Be(800m, "la PPD se aplica con su REP");
        p.ReppTimbrado.Should().BeFalse("la parte PPD sigue esperando su REP");
    }

    [Fact]
    public async Task PUE_ya_cobrada_en_caja_no_se_vuelve_a_aplicar()
    {
        using var db = Db();
        var pue = Factura(db, "FX-000004", 300m, "PUE");
        var p = Propuesta(db, (pue, 300m, null));
        await db.SaveChangesAsync();
        // Cobro de mostrador que llegó a cartera antes de la confirmación (carrera de eventos).
        pue.AplicarPago(300m);
        db.MovimientosCartera.Add(new MovimientoCartera(EmpresaId, pue.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), 300m, Ahora));
        await db.SaveChangesAsync();

        await Confirmar(db, p);

        pue.MontoPagado.Should().Be(300m);
        (await db.MovimientosCartera.CountAsync(m => m.OrigenComprobanteId == p.Id)).Should().Be(0);
        (await db.EventosProcesados.SingleAsync()).Detalle.Should().Contain("sin saldo");
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeEmpresaContext(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
