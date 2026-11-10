using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Facturacion.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// M-209 (86e3zr7mp) · IVA por tratamiento con el motor real y las plantillas de la migración: la factura de proveedor con 16 % y
/// exento lleva solo el IVA del 16 % y su saldo guarda las dos bases (CA1); la factura de venta al 8 % lleva su IVA al rol de la tasa,
/// que sin cuenta propia cae al rol base (CA2). Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    [Fact]
    public async Task M209_CA1_factura_de_proveedor_con_16_y_exento_separa_el_iva_y_guarda_las_bases()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), 14_100m,
                [new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, 12_500m)], [], ProveedorId: e.Proveedor, Uuid: $"UUID-{factura:N}",
                Subtotal: 12_500m, Iva: 1_600m, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: e.Sucursal,
                Impuestos: [new ImpuestoTrasladoPayload("TASA16", 10_000m, 1_600m), new ImpuestoTrasladoPayload("EXENTO", 2_500m, 0m)]));

            var tt = (await PolizasAsync(factura)).Should().ContainSingle().Subject;
            tt.Partidas.Where(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PENDIENTE"]).Sum(x => x.Cargo).Should().Be(1_600m);
            tt.Partidas.Sum(x => x.Cargo).Should().Be(tt.Partidas.Sum(x => x.Abono));

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var saldo = await db.SaldosIvaDocumento.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.DocumentoId == factura);
            saldo.Tratamientos.Select(t => (t.Tratamiento, t.Base, t.Iva))
                .Should().BeEquivalentTo(new[] { ("TASA16", 10_000m, 1_600m), ("EXENTO", 2_500m, 0m) });
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task M209_CA2_factura_de_venta_al_8_lleva_el_iva_trasladado_de_la_tasa()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            await Facturacion(FacturaVenta(e, factura, e.Clientes[0], 5_000m, 400m) with
            {
                Impuestos = [new ImpuestoTrasladoContablePayload("TASA8", 5_000m, 400m)],
            });

            var rf = (await PolizasAsync(factura)).Should().ContainSingle().Subject;
            rf.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_TRASLADADO_NO_COBRADO"] && x.Abono == 400m,
                "IVA_TRASLADADO_NO_COBRADO_TASA8 no tiene cuenta: cae al rol base");

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            var saldo = await db.SaldosIvaDocumento.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.DocumentoId == factura);
            saldo.Tratamientos.Should().ContainSingle(t => t.Tratamiento == "TASA8" && t.Base == 5_000m && t.Iva == 400m);
        }
        finally { await LimpiarAsync(e); }
    }
}
