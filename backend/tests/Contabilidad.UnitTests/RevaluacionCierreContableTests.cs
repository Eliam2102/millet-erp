using Millet.Contabilidad.Application.Revaluacion;
using M = Millet.Contabilidad.Application.Revaluacion.CalculadoraRevaluacionCierre.MovimientoMoneda;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// CON-11 (86e3kdypd) · CA10.8: revaluación de cierre de bancos y clientes en moneda extranjera al TC del último día; la póliza ajusta cada
/// cuenta contra utilidad o pérdida cambiaria no realizada. Datos FIX.
/// </summary>
public class RevaluacionCierreContableTests
{
    private static readonly Guid Cuenta = Guid.NewGuid(), Sucursal = Guid.NewGuid(), Cliente = Guid.NewGuid();
    private static readonly Guid PerdidaBancos = Guid.NewGuid(), UtilidadBancos = Guid.NewGuid();
    private static readonly Guid PerdidaClientes = Guid.NewGuid(), UtilidadClientes = Guid.NewGuid();

    /// <summary>Catálogo de Contabilidad: subcuenta de pérdida y utilidad no realizada por origen (bancos .03, clientes .01).</summary>
    private static readonly Dictionary<ControlRevaluacion, (Guid Perdida, Guid Utilidad)> Cuentas = new()
    {
        [ControlRevaluacion.Bancos] = (PerdidaBancos, UtilidadBancos),
        [ControlRevaluacion.Clientes] = (PerdidaClientes, UtilidadClientes),
    };

    private static M Cargo(decimal mxn, decimal usd) => new(Cuenta, Sucursal, null, null, "USD", mxn, 0m, usd);
    private static M Abono(decimal mxn, decimal usd) => new(Cuenta, Sucursal, null, null, "USD", 0m, mxn, usd);

    private static RevaluacionCierreDetalle Detalle(ControlRevaluacion control, decimal usd, decimal libros, decimal tc, Guid? cliente = null)
    {
        var cierre = Math.Round(usd * tc, 2, MidpointRounding.AwayFromZero);
        return new(control, Cuenta, "102.02", Sucursal, cliente, null, "USD", usd, libros, tc, cierre, cierre - libros);
    }

    [Fact]
    public void El_saldo_en_moneda_y_en_libros_suma_cargos_y_resta_abonos()
    {
        var (usd, mxn) = CalculadoraRevaluacionCierre.Saldos([Cargo(18_000m, 1_000m), Abono(3_700m, 200m)]);
        (usd, mxn).Should().Be((800m, 14_300m));
    }

    [Fact]
    public void Una_anulacion_con_importes_negativos_resta_su_importe_original()
    {
        // K10.3: la anulación conserva el lado y lleva el importe en negativo.
        var (usd, mxn) = CalculadoraRevaluacionCierre.Saldos([Cargo(18_000m, 1_000m), Cargo(-18_000m, 1_000m)]);
        (usd, mxn).Should().Be((0m, 0m));
    }

    [Fact]
    public void CA10_8_banco_en_dolares_a_tc_de_cierre_mayor_registra_utilidad_no_realizada()
    {
        var d = Detalle(ControlRevaluacion.Bancos, 1_000m, 18_000m, 18.50m);
        d.Diferencia.Should().Be(500m);
        d.Resultado.Should().Be("Utilidad");

        var partidas = CalculadoraRevaluacionCierre.Partidas([d], Sucursal, Cuentas);
        partidas.Should().HaveCount(2);
        partidas[0].Should().Match<Millet.Contabilidad.Application.Polizas.PartidaRequest>(p => p.CuentaId == Cuenta && p.Cargo == 500m && p.Abono == 0m);
        partidas[1].Should().Match<Millet.Contabilidad.Application.Polizas.PartidaRequest>(p => p.CuentaId == UtilidadBancos && p.Abono == 500m);
    }

    [Fact]
    public void CA10_8_cliente_en_dolares_a_tc_de_cierre_menor_registra_perdida_con_su_auxiliar()
    {
        var d = Detalle(ControlRevaluacion.Clientes, 1_000m, 18_000m, 17.80m, Cliente);
        d.Diferencia.Should().Be(-200m);

        var partidas = CalculadoraRevaluacionCierre.Partidas([d], Sucursal, Cuentas);
        partidas[0].Should().Match<Millet.Contabilidad.Application.Polizas.PartidaRequest>(p =>
            p.CuentaId == Cuenta && p.Abono == 200m && p.ClienteId == Cliente);
        partidas[1].Should().Match<Millet.Contabilidad.Application.Polizas.PartidaRequest>(p => p.CuentaId == PerdidaClientes && p.Cargo == 200m);
    }

    [Fact]
    public void La_poliza_de_varios_saldos_cuadra_y_netea_por_lado()
    {
        var detalles = new[]
        {
            Detalle(ControlRevaluacion.Bancos, 1_000m, 18_000m, 18.50m),
            Detalle(ControlRevaluacion.Clientes, 2_000m, 37_000m, 18.50m, Cliente),
            Detalle(ControlRevaluacion.Clientes, 300m, 5_550m, 18.50m, Guid.NewGuid()),
        };
        var partidas = CalculadoraRevaluacionCierre.Partidas(detalles, Sucursal, Cuentas);
        partidas.Sum(p => p.Cargo).Should().Be(partidas.Sum(p => p.Abono));
        partidas.Single(p => p.CuentaId == UtilidadBancos).Abono.Should().Be(500m);
        partidas.Should().NotContain(p => p.CuentaId == PerdidaBancos || p.CuentaId == UtilidadClientes || p.CuentaId == PerdidaClientes,
            "el saldo que no cambia no genera partida");
    }

    [Fact]
    public void Bancos_y_clientes_van_cada_uno_a_su_cuenta_de_resultado_sin_netear_entre_si()
    {
        var detalles = new[]
        {
            Detalle(ControlRevaluacion.Bancos, 1_000m, 18_000m, 18.50m),               // +500 utilidad bancos
            Detalle(ControlRevaluacion.Clientes, 1_000m, 19_000m, 18.50m, Cliente),   // −500 pérdida clientes
        };
        var partidas = CalculadoraRevaluacionCierre.Partidas(detalles, Sucursal, Cuentas);
        partidas.Single(p => p.CuentaId == UtilidadBancos).Abono.Should().Be(500m);
        partidas.Single(p => p.CuentaId == PerdidaClientes).Cargo.Should().Be(500m);
        partidas.Sum(p => p.Cargo).Should().Be(partidas.Sum(p => p.Abono));
    }

    [Fact]
    public void Los_roles_de_resultado_llevan_el_sufijo_del_control()
    {
        CalculadoraRevaluacionCierre.RolResultado(CalculadoraRevaluacionCierre.RolPerdida, ControlRevaluacion.Bancos)
            .Should().Be("PERDIDA_CAMBIARIA_NO_REALIZADA_BANCOS");
        CalculadoraRevaluacionCierre.RolResultado(CalculadoraRevaluacionCierre.RolUtilidad, ControlRevaluacion.Clientes)
            .Should().Be("UTILIDAD_CAMBIARIA_NO_REALIZADA_CLIENTES");
    }

    [Fact]
    public void El_cierre_es_el_ultimo_dia_y_la_reversa_el_1_del_mes_siguiente()
    {
        CalculadoraRevaluacionCierre.Cierre(2027, 1).Should().Be(new DateOnly(2027, 1, 31));
        CalculadoraRevaluacionCierre.Cierre(2027, 1).AddDays(1).Should().Be(new DateOnly(2027, 2, 1));
        CalculadoraRevaluacionCierre.Cierre(2028, 2).Should().Be(new DateOnly(2028, 2, 29));
    }

    [Fact]
    public void El_documento_del_mes_es_estable_por_empresa_y_mes()
    {
        var e = Guid.NewGuid();
        CalculadoraRevaluacionCierre.DocumentoId(e, 2027, 1).Should().Be(CalculadoraRevaluacionCierre.DocumentoId(e, 2027, 1));
        CalculadoraRevaluacionCierre.DocumentoId(e, 2027, 1).Should().NotBe(CalculadoraRevaluacionCierre.DocumentoId(e, 2027, 2));
    }
}
