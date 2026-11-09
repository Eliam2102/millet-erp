using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Repp;

/// <summary>
/// CA7.10 (CXC-06, V-CXC-11): un cobro con ajuste no fiscal autorizado (comisión bancaria) llega con lo aplicado mayor que el
/// depósito. El REP relaciona lo cobrado; la diferencia no lleva CFDI y CxC la aplica en cartera.
/// </summary>
public sealed class ReppAjusteNoFiscalTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Empresa = Guid.NewGuid();

    [Fact]
    public async Task CA7_10_el_pendiente_del_REP_relaciona_el_deposito_de_9952_y_no_los_10000_aplicados()
    {
        using var db = new FacturacionDbContext(new DbContextOptionsBuilder<FacturacionDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(Empresa));
        var factura = Guid.NewGuid();
        var payload = new PagoClienteConfirmadoPayload(Empresa, Ahora, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            9_952m, "USD", new(2026, 11, 20), "DEP-EU", [new(factura, 10_000m)]);

        await new EmitirReppDesdePagoConfirmadoHandler(db, new FakeClock(Ahora))
            .Handle(new EmitirReppDesdePagoConfirmadoCommand(Guid.NewGuid(), payload), default);

        db.ChangeTracker.Clear();
        var pendiente = await db.ReppPendientes.Include(p => p.Facturas).SingleAsync();
        pendiente.Monto.Should().Be(9_952m);
        pendiente.Facturas.Should().ContainSingle().Which.Importe.Should().Be(9_952m);
        var relacion = pendiente.Facturas.Select(f => new RelacionRepp(f.FacturaVentaId, f.Importe)).ToArray();
        pendiente.Invoking(p => p.ValidarRelacion(relacion)).Should().NotThrow("antes fallaba con REPP_MONTO_DISTINTO");
    }

    [Fact]
    public void Con_varias_facturas_el_ajuste_se_descuenta_de_las_ultimas_y_sin_exceso_la_relacion_no_cambia()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        EmitirReppDesdePagoConfirmadoHandler.RelacionFiscal([new(a, 6_000m), new(b, 4_000m)], 9_952m)
            .Should().Equal(new RelacionRepp(a, 6_000m), new RelacionRepp(b, 3_952m));
        EmitirReppDesdePagoConfirmadoHandler.RelacionFiscal([new(a, 9_970m), new(b, 20m), new(c, 10m)], 9_952m)
            .Should().Equal(new RelacionRepp(a, 9_952m));
        EmitirReppDesdePagoConfirmadoHandler.RelacionFiscal([new(a, 6_000m), new(b, 4_000m)], 10_000m)
            .Should().Equal(new RelacionRepp(a, 6_000m), new RelacionRepp(b, 4_000m));
        EmitirReppDesdePagoConfirmadoHandler.RelacionFiscal(null, 100m).Should().BeEmpty();
    }
}
