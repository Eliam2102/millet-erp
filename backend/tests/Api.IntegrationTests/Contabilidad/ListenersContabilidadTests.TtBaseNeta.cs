using Millet.Contabilidad.Domain;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Tesoreria.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// Cadena factura de proveedor → pago: la factura con descuento (base neta en ValorCapitalizable) y centavos de más dentro de la
/// tolerancia genera su TT cuadrada (puente neto, REDONDEO por la diferencia) y el pago que la esperaba sale de «En espera» y
/// genera su PP. Antes la TT no cuadraba y el pago quedaba pendiente para siempre. Datos ficticios.
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    [Fact]
    public async Task TT_la_factura_con_descuento_y_centavos_genera_su_TT_y_el_pago_en_espera_genera_su_PP()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var pago = Guid.NewGuid();

            // El pago llega primero: espera la TT de la factura.
            var eventoPago = await Tesoreria(new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, pago, 11_020.40m,
                "MXN", Hoy(), "Transferencia", null));
            var rechazo = await RechazoAsync(eventoPago);
            rechazo!.Causa.Should().Be(CausaRechazoContable.DatoFaltante);
            rechazo.Detalle.Should().Be($"Espera:{factura}");

            // CFDI: 10,000 − descuento 500 = 9,500 + IVA 1,520 = 11,020; capturado 11,020.40 (CxP lo acepta dentro de $0.99).
            await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), 11_020.40m,
                [
                    new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, 6_000m, ValorCapitalizable: 5_700m),
                    new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, 4_000m, ValorCapitalizable: 3_800m),
                ], [], ProveedorId: e.Proveedor, Subtotal: 10_000m, Iva: 1_520m, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: e.Sucursal));

            var tt = (await PolizasAsync(factura)).Should().ContainSingle().Subject;
            tt.TipoClave.Should().Be("TT");
            tt.Partidas.Sum(x => x.Cargo).Should().Be(tt.Partidas.Sum(x => x.Abono)).And.Be(11_020.40m);
            Saldo(tt.Partidas, e.Cuentas["MERCANCIA_RECIBIDA_POR_FACTURAR"]).Should().Be(9_500m, "el puente va neto del descuento");
            Saldo(tt.Partidas, e.Cuentas["REDONDEO"]).Should().Be(0.40m);
            Saldo(tt.Partidas, e.Cuentas["PROVEEDORES"]).Should().Be(-11_020.40m);

            var pp = (await PolizasAsync(pago)).Should().ContainSingle().Subject;
            pp.TipoClave.Should().Be("PP");
            pp.Partidas.Sum(x => x.Cargo).Should().Be(pp.Partidas.Sum(x => x.Abono));
            (await RechazoAsync(eventoPago))!.Estado.Should().Be(EstadoRechazoContable.Reprocesado);
            Saldo([.. tt.Partidas, .. pp.Partidas], e.Cuentas["PROVEEDORES"]).Should().Be(0m, "el pago salda al proveedor");
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task PP_el_pago_SPEI_abona_a_la_cuenta_contable_de_su_cuenta_bancaria_y_no_a_la_del_rol_BANCOS()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var pago = Guid.NewGuid();
            await Cxp(new FacturaProveedorRegistradaIntegrationEvent(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), 1_160m,
                [new LineaFacturadaPayload(Guid.NewGuid(), Guid.NewGuid(), 1m, 1_000m, ValorCapitalizable: 1_000m)], [],
                ProveedorId: e.Proveedor, Subtotal: 1_000m, Iva: 160m, RetencionesTotal: 0m, Moneda: "MXN", SucursalId: e.Sucursal));

            // B8: Tesorería manda la cuenta contable de la cuenta bancaria de Millet de la que salió el SPEI (aquí, la del rol BANCOS_MN).
            var eventoPago = await Tesoreria(new PagoFacturaProveedorAplicadoIntegrationEvent(EmpresaBootstrapId, Ahora, factura, pago, 1_160m,
                "MXN", Hoy(), "Transferencia", "SPEI-B8") { CuentaContableRef = Codigo(e.Suf, "1.4") });

            (await RechazoAsync(eventoPago)).Should().BeNull("la cuenta de bancos viene en el evento");
            var pp = (await PolizasAsync(pago)).Should().ContainSingle().Subject;
            pp.TipoClave.Should().Be("PP");
            Saldo(pp.Partidas, e.Cuentas["BANCOS_MN"]).Should().Be(-1_160m);
            pp.Partidas.Should().NotContain(x => x.CuentaId == e.Cuentas["BANCOS"]);
        }
        finally { await LimpiarAsync(e); }
    }
}
