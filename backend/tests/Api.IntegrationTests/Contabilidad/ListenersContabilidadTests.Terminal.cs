using Millet.Facturacion.Application.Integration;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// 86e3zcbjx · póliza de la factura mensual del banco por las comisiones de la terminal de tarjetas, con el motor real y la plantilla
/// sembrada por la migración: comisión e IVA pagado del CFDI contra el banco de la terminal y el centavo de diferencia a redondeo.
/// Datos FICTICIOS (cargos del mes: comisión 6,000 e IVA 960).
/// </summary>
public sealed partial class ListenersContabilidadTests
{
    private static TerminalFacturaBancoLigadaIntegrationEvent FacturaBanco(Escenario e, Guid factura, decimal comisionFactura, decimal ivaFactura) =>
        new(EmpresaBootstrapId, Ahora, factura, Guid.NewGuid(), $"{Guid.NewGuid()}".ToUpperInvariant(), "BAN010101AAA", Hoy().Year, Hoy().Month,
            6_000m, 960m, 6_960m,
            [new CargoTerminalFacturado(Guid.NewGuid(), "LIQ-FIX-1", new DateOnly(Hoy().Year, Hoy().Month, 1), 6_000m, 960m)],
            comisionFactura, ivaFactura, 6_960m - comisionFactura - ivaFactura, e.Sucursal);

    [Theory]
    [InlineData(6_000.01, -0.01)] // la factura trae un centavo más que los cargos: abono a redondeo
    [InlineData(5_999.99, 0.01)]  // un centavo menos: cargo a redondeo
    public async Task T86e3zcbjx_la_factura_del_banco_registra_comision_e_iva_pagado_contra_el_banco_y_el_centavo_a_redondeo(
        double comision, double redondeo)
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var evento = await Facturacion(FacturaBanco(e, factura, (decimal)comision, 960m));

            (await RechazoAsync(evento)).Should().BeNull();
            var poliza = (await PolizasAsync(factura)).Should().ContainSingle().Subject;
            poliza.TipoClave.Should().Be("AS");
            poliza.Partidas.Should().HaveCount(4);
            poliza.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["COMISIONES_BANCARIAS"] && x.Cargo == (decimal)comision);
            poliza.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["IVA_ACREDITABLE_PAGADO"] && x.Cargo == 960m);
            poliza.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["BANCO_TERMINAL_TARJETA"] && x.Abono == 6_960m);
            var r = (decimal)redondeo;
            poliza.Partidas.Should().Contain(x => x.CuentaId == e.Cuentas["REDONDEO"]
                && x.Cargo == Math.Max(0m, r) && x.Abono == Math.Max(0m, -r));
            poliza.Partidas.Sum(x => x.Cargo).Should().Be(poliza.Partidas.Sum(x => x.Abono));
        }
        finally { await LimpiarAsync(e); }
    }

    [Fact]
    public async Task T86e3zcbjx_al_centavo_no_hay_partida_de_redondeo_y_el_evento_repetido_no_duplica()
    {
        Escenario? e = null;
        try
        {
            e = await PrepararAsync();
            var factura = Guid.NewGuid();
            var evento = FacturaBanco(e, factura, 6_000m, 960m);
            var id = await Facturacion(evento);
            await Facturacion(evento, id);

            var poliza = (await PolizasAsync(factura)).Should().ContainSingle().Subject;
            poliza.Partidas.Should().HaveCount(3);
            poliza.Partidas.Should().NotContain(x => x.CuentaId == e.Cuentas["REDONDEO"]);
        }
        finally { await LimpiarAsync(e); }
    }
}
