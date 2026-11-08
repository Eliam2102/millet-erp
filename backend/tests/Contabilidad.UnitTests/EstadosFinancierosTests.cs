using System.Text.Json;
using Millet.Contabilidad.Application.Reportes;
using Millet.Contabilidad.Domain;
using H = Millet.Contabilidad.Application.Reportes.EstadoFinancieroHandler;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// F1-CON-08 · K10.10: balance general y estado de resultados desde los saldos por cuenta y los rubros del catálogo (P24), con la
/// estructura del catálogo del 5-nov (rubros 100 activo circulante, 200 pasivo, 300 capital, 400 ingresos, 500 costo, 600 gastos,
/// 700 financiamiento, 800 cuentas de orden).
/// </summary>
public sealed class EstadosFinancierosTests
{
    private const string Balance = "Estado de Posicion Financiera (Balance)";
    private const string Resultado = "Estado de Pérdidas y Ganancias (Resultado)";
    private const string Notas = "Notas a los Estados Financieros";

    private static CuentaContable Rubro(string codigo, string nombre, string grupo) =>
        new(Guid.NewGuid(), codigo, nombre, null, 1, null, TipoCuenta.Titulo, CuentaControl.Ninguna, null, grupo, ClaseCuenta.Rubro);

    private static CuentaContable Mayor(string codigo, string nombre, CuentaContable? rubro, string? grupo, NaturalezaCuenta n)
    {
        var c = new CuentaContable(Guid.NewGuid(), codigo, nombre, null, 1, n, TipoCuenta.Titulo, CuentaControl.Ninguna, null, grupo);
        c.AsignarRubro(rubro?.Id);
        return c;
    }

    private static CuentaContable Hija(string codigo, string nombre, CuentaContable padre) =>
        new(Guid.NewGuid(), codigo, nombre, padre.Id, 2, padre.Naturaleza, TipoCuenta.Afectable, CuentaControl.Ninguna, null, padre.GrupoReporte);

    private static readonly CuentaContable RActivo = Rubro("100.00.00.00", "ACTIVO CIRCULANTE", Balance);
    private static readonly CuentaContable RPasivo = Rubro("200.00.00.00", "PASIVO A CORTO PLAZO", Balance);
    private static readonly CuentaContable RCapital = Rubro("300.00.00.00", "CAPITAL", Balance);
    private static readonly CuentaContable RIngresos = Rubro("400.00.00.00", "INGRESOS", Resultado);
    private static readonly CuentaContable RCosto = Rubro("500.00.00.00", "COSTO", Resultado);
    private static readonly CuentaContable RGastos = Rubro("600.00.00.00", "GASTOS DE OPERACIÓN", Resultado);
    private static readonly CuentaContable RFin = Rubro("700.00.00.00", "COSTO DE FINANCIAMIENTO", Resultado);
    private static readonly CuentaContable ROrden = Rubro("800.00.00.00", "CUENTAS DE ORDEN", Notas);

    private static readonly CuentaContable Bancos = Mayor("102.00.00.00", "BANCOS", RActivo, Balance, NaturalezaCuenta.Deudora);
    private static readonly CuentaContable BancoA = Hija("102.01.00.00", "Banco A", Bancos);
    private static readonly CuentaContable Clientes = Mayor("105.00.00.00", "CLIENTES", RActivo, Balance, NaturalezaCuenta.Deudora);
    private static readonly CuentaContable Proveedores = Mayor("201.00.00.00", "PROVEEDORES", RPasivo, Balance, NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable CapitalSocial = Mayor("301.00.00.00", "CAPITAL SOCIAL", RCapital, Balance, NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable Ventas = Mayor("401.00.00.00", "VENTAS", RIngresos, Resultado, NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable CostoVentas = Mayor("501.00.00.00", "COSTO DE VENTAS", RCosto, Resultado, NaturalezaCuenta.Deudora);
    private static readonly CuentaContable GastosAdm = Mayor("601.00.00.00", "GASTOS DE ADMINISTRACIÓN", RGastos, Resultado, NaturalezaCuenta.Deudora);
    private static readonly CuentaContable PerdidaCamb = Mayor("701.00.00.00", "PÉRDIDA CAMBIARIA", RFin, Resultado, NaturalezaCuenta.Deudora);
    private static readonly CuentaContable Orden = Mayor("801.00.00.00", "CUENTAS DE ORDEN DEUDORAS", ROrden, Notas, NaturalezaCuenta.Deudora);
    private static readonly CuentaContable SinRubro = Mayor("999.00.00.00", "CUENTA NUEVA", null, null, NaturalezaCuenta.Deudora);

    private static readonly CuentaContable[] Catalogo =
        [RActivo, RPasivo, RCapital, RIngresos, RCosto, RGastos, RFin, ROrden, Bancos, BancoA, Clientes, Proveedores, CapitalSocial, Ventas,
            CostoVentas, GastosAdm, PerdidaCamb, Orden, SinRubro];

    /// <summary>
    /// Noviembre simulado (saldos deudores al corte; la suma es cero como en la balanza): capital 10,000 en el banco; venta 5,000 a
    /// crédito; costo 3,000 y gastos 1,000 a proveedores; pérdida cambiaria 200 pagada del banco. Ejercicio anterior: utilidad 500 aún
    /// en ventas (sin cierre anual), cobrada en el banco.
    /// </summary>
    private static Dictionary<Guid, decimal> SaldosNoviembre() => new()
    {
        [BancoA.Id] = 10_000m - 200m + 500m,
        [Clientes.Id] = 5_000m,
        [Proveedores.Id] = -4_000m,
        [CapitalSocial.Id] = -10_000m,
        [Ventas.Id] = -5_000m - 500m,
        [CostoVentas.Id] = 3_000m,
        [GastosAdm.Id] = 1_000m,
        [PerdidaCamb.Id] = 200m,
        [Orden.Id] = 7_777m,
    };

    private static decimal? Importe(H.Armado a, string concepto) => a.Filas.Single(f => f.Concepto == concepto).Actual;

    [Fact]
    public void K10_10_a_el_balance_cuadra_activo_igual_a_pasivo_mas_capital_con_el_resultado_del_ejercicio()
    {
        var saldos = SaldosNoviembre();
        saldos.Remove(Orden.Id); // las cuentas de orden van en par y no entran
        var antes = new Dictionary<Guid, decimal> { [Ventas.Id] = -500m, [BancoA.Id] = 500m };
        var a = H.Armar(EstadoFinanciero.BalanceGeneral, Catalogo, new(saldos, antes), null);

        a.Totales.Activo.Should().Be(15_300m);
        a.Totales.Pasivo.Should().Be(4_000m);
        a.Totales.Capital.Should().Be(11_300m, "capital social 10,000 + ejercicios anteriores 500 + resultado del ejercicio 800");
        a.Totales.Cuadra.Should().BeTrue();
        Importe(a, "Resultado del ejercicio").Should().Be(800m);
        Importe(a, "Resultados de ejercicios anteriores (sin cierre anual)").Should().Be(500m);
        Importe(a, "BANCOS").Should().Be(10_300m, "la hija suma en su cuenta de mayor");
        Importe(a, "Total pasivo y capital").Should().Be(15_300m);
        a.Avisos.Should().BeEmpty();
        a.Filas.Should().NotContain(f => f.Concepto.Contains("ORDEN"), "las cuentas de orden no entran a los estados");
    }

    [Fact]
    public void K10_10_a_el_estado_de_resultados_cuadra_con_la_balanza()
    {
        var saldos = new Dictionary<Guid, decimal>
        {
            [Ventas.Id] = -5_000m, [CostoVentas.Id] = 3_000m, [GastosAdm.Id] = 1_000m, [PerdidaCamb.Id] = 200m,
            [BancoA.Id] = 800m, // las de balance no entran al estado de resultados
        };
        var a = H.Armar(EstadoFinanciero.EstadoResultados, Catalogo, new(saldos, new Dictionary<Guid, decimal>()), null);

        a.Totales.Ingresos.Should().Be(5_000m);
        a.Totales.UtilidadBruta.Should().Be(2_000m);
        a.Totales.UtilidadOperacion.Should().Be(1_000m);
        a.Totales.UtilidadPeriodo.Should().Be(800m, "= −Σ de los saldos de resultados en la balanza");
        Importe(a, "Utilidad (pérdida) del periodo").Should().Be(800m);
        a.Filas.Single(f => f.Concepto == "COSTO DE VENTAS").Porcentaje.Should().Be(60.0m);
        a.Filas.Should().NotContain(f => f.Concepto == "BANCOS");
        a.Filas.Select(f => f.Concepto).Should().ContainInOrder(
            "INGRESOS", "Total ingresos", "COSTO", "Total costo", "Utilidad (pérdida) bruta", "GASTOS DE OPERACIÓN",
            "Utilidad (pérdida) de operación", "COSTO DE FINANCIAMIENTO", "Utilidad (pérdida) del periodo");
    }

    [Fact]
    public void K10_10_b_una_cuenta_sin_rubro_se_senala_y_no_se_omite()
    {
        var saldos = new Dictionary<Guid, decimal> { [BancoA.Id] = 1_000m, [SinRubro.Id] = 250m, [CapitalSocial.Id] = -1_250m };
        var a = H.Armar(EstadoFinanciero.BalanceGeneral, Catalogo, new(saldos, new Dictionary<Guid, decimal>()), null);

        a.SinClasificar.Should().ContainSingle().Which.Codigo.Should().Be("999.00.00.00");
        a.Filas.Should().Contain(f => f.Tipo == "cuenta" && f.Cuenta == "999.00.00.00" && f.Actual == 250m);
        a.Avisos.Should().Contain(m => m.Contains("999.00.00.00") && m.Contains("Sin clasificar"));
        a.Totales.Cuadra.Should().BeFalse();
        a.Totales.Diferencia.Should().Be(-250m);
        a.Totales.SinClasificar.Should().Be(250m, "la diferencia la explican las cuentas sin clasificar");
    }

    [Fact]
    public void El_comparativo_trae_el_anio_anterior_y_la_variacion()
    {
        var actual = new Dictionary<Guid, decimal> { [Ventas.Id] = -5_000m, [CostoVentas.Id] = 3_000m };
        var anterior = new Dictionary<Guid, decimal> { [Ventas.Id] = -4_000m, [GastosAdm.Id] = 500m };
        var a = H.Armar(EstadoFinanciero.EstadoResultados, Catalogo, new(actual, new Dictionary<Guid, decimal>()),
            new(anterior, new Dictionary<Guid, decimal>()));

        var ventas = a.Filas.Single(f => f.Concepto == "VENTAS");
        (ventas.Actual, ventas.Comparativo, ventas.Variacion).Should().Be((5_000m, 4_000m, 1_000m));
        var gastos = a.Filas.Single(f => f.Concepto == "GASTOS DE ADMINISTRACIÓN");
        (gastos.Actual, gastos.Comparativo).Should().Be((0m, 500m), "una cuenta con saldo solo en el comparativo también sale");
        a.TotalesComparativo!.UtilidadPeriodo.Should().Be(3_500m);
        a.Totales.UtilidadPeriodo.Should().Be(2_000m);
    }

    [Theory]
    [InlineData(Balance, "100.00.00.00", SeccionEstadoFinanciero.Activo)]
    [InlineData(Balance, "250.00.00.00", SeccionEstadoFinanciero.Pasivo)]
    [InlineData(Balance, "300.00.00.00", SeccionEstadoFinanciero.Capital)]
    [InlineData(Resultado, "400.00.00.00", SeccionEstadoFinanciero.Ingresos)]
    [InlineData(Resultado, "FIX-600.00.00.00", SeccionEstadoFinanciero.Gastos)]
    [InlineData(Resultado, "700.00.00.00", SeccionEstadoFinanciero.Financiamiento)]
    [InlineData(Resultado, "900.00.00.00", SeccionEstadoFinanciero.OtrosResultados)]
    public void La_seccion_sale_del_grupo_y_del_primer_digito_del_rubro(string grupo, string codigo, SeccionEstadoFinanciero esperada) =>
        H.Seccion(H.Grupo(grupo)!, codigo).Should().Be(esperada);

    [Fact]
    public void Los_grupos_del_catalogo_se_reconocen_sin_importar_acentos() =>
        (H.Grupo(Balance), H.Grupo("Estado de Perdidas y Ganancias (Resultado)"), H.Grupo(Notas), H.Grupo("Otro"))
            .Should().Be(("balance", "resultados", "notas", (string?)null));

    [Fact]
    public void Los_filtros_se_leen_del_texto_y_responden_400_en_espanol()
    {
        var q = FiltrosEstadoFinanciero.Leer(EstadoFinanciero.EstadoResultados, null, null, "false", "true", null, new DateOnly(2026, 11, 7));
        (q.Anio, q.Periodo, q.Acumulado, q.Comparar, q.Desde).Should().Be((2026, 11, false, true, 11));

        var error = FluentActions.Invoking(() =>
                FiltrosEstadoFinanciero.Leer(EstadoFinanciero.BalanceGeneral, "dos mil", "13", null, "si", "MID"))
            .Should().Throw<Millet.SharedKernel.Application.Exceptions.ValidationException>().Which.Errors;
        error.Select(e => e.Field).Should().BeEquivalentTo("anio", "comparar", "sucursalId");
        error.Should().OnlyContain(e => !e.Message.Contains("Failed") && !e.Message.Contains("must"));
    }

    [Fact]
    public void El_validador_responde_en_espanol()
    {
        var r = new EstadoFinancieroValidator().Validate(new EstadoFinancieroQuery(EstadoFinanciero.BalanceGeneral, 1990, 14));
        r.Errors.Select(e => e.ErrorMessage).Should().Contain(m => m.StartsWith("El año")).And.Contain(m => m.StartsWith("El periodo"));
    }
    // ---- Correcciones del QA de CON-08 (8-nov) ----

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void H1_los_enums_de_la_respuesta_viajan_como_texto()
    {
        var json = JsonSerializer.Serialize(new { Integracion = IntegracionContable.Gastos, Estado = EstadoFinanciero.EstadoResultados }, Web);
        json.Should().Be("""{"integracion":"Gastos","estado":"EstadoResultados"}""", "la pantalla hace tipo.toLowerCase()");
    }

    [Fact]
    public void O2_un_rubro_con_el_nombre_de_su_seccion_no_repite_el_encabezado()
    {
        var saldos = new Dictionary<Guid, decimal> { [Ventas.Id] = -5_000m, [CostoVentas.Id] = 3_000m };
        var a = H.Armar(EstadoFinanciero.EstadoResultados, Catalogo, new(saldos, new Dictionary<Guid, decimal>()), null);

        a.Filas.Count(f => f.Concepto == "INGRESOS").Should().Be(1, "rubro INGRESOS = sección Ingresos");
        a.Filas.Should().NotContain(f => f.Tipo == "rubro" && (f.Concepto == "INGRESOS" || f.Concepto == "COSTO"));
        a.Filas.Should().NotContain(f => f.Concepto == "Total INGRESOS", "con un solo rubro, el total de la sección basta");
        a.Filas.Single(f => f.Concepto == "VENTAS").Sangria.Should().Be(1);
        Importe(a, "Total ingresos").Should().Be(5_000m);
    }

    [Theory]
    [InlineData(-1_680, "Total sin clasificar (saldo acreedor)")]
    [InlineData(250, "Total sin clasificar (saldo deudor)")]
    public void O3_el_total_sin_clasificar_dice_deudor_o_acreedor_segun_su_signo(int saldo, string etiqueta)
    {
        var saldos = new Dictionary<Guid, decimal> { [SinRubro.Id] = saldo, [BancoA.Id] = -saldo };
        var a = H.Armar(EstadoFinanciero.BalanceGeneral, Catalogo, new(saldos, new Dictionary<Guid, decimal>()), null);
        a.Filas.Single(f => f.Clave == "TS:sin").Should().Match<FilaEstadoFinanciero>(f => f.Concepto == etiqueta && f.Actual == saldo);
    }

    [Fact]
    public void O4_en_resultados_solo_sale_sin_clasificar_la_cuenta_de_resultados_y_la_otra_va_en_un_aviso()
    {
        var gastoSinRubro = Mayor("650.00.00.00", "GASTO NUEVO", null, null, NaturalezaCuenta.Deudora);
        var saldos = new Dictionary<Guid, decimal> { [Ventas.Id] = -5_000m, [SinRubro.Id] = -1_680m, [gastoSinRubro.Id] = 300m };
        var a = H.Armar(EstadoFinanciero.EstadoResultados, [.. Catalogo, gastoSinRubro], new(saldos, new Dictionary<Guid, decimal>()), null);

        a.Filas.Should().Contain(f => f.Cuenta == "650.00.00.00", "por su código (6) es de resultados");
        a.Filas.Should().NotContain(f => f.Cuenta == "999.00.00.00");
        a.SinClasificar.Should().ContainSingle().Which.Codigo.Should().Be("650.00.00.00");
        a.Totales.SinClasificar.Should().Be(300m);
        a.Avisos.Should().Contain(m => m.Contains("999.00.00.00") && m.Contains("balance general"));

        var balance = H.Armar(EstadoFinanciero.BalanceGeneral, [.. Catalogo, gastoSinRubro], new(saldos, new Dictionary<Guid, decimal>()), null);
        balance.Filas.Should().Contain(f => f.Cuenta == "999.00.00.00", "en el balance siguen todas las que explican la diferencia");
    }

    [Fact]
    public void O6_el_estado_de_resultados_del_ejercicio_al_periodo_12_no_incluye_el_periodo_13()
    {
        var movs = new[]
        {
            new MovimientoContable { CuentaId = Ventas.Id, PeriodoAnio = 2026, PeriodoNumero = 12, Abono = 5_000m },
            new MovimientoContable { CuentaId = Ventas.Id, PeriodoAnio = 2026, PeriodoNumero = 13, Cargo = 5_000m }, // p. ej. cierre anual
            new MovimientoContable { CuentaId = Ventas.Id, PeriodoAnio = 2025, PeriodoNumero = 12, Abono = 9_000m },
        }.AsQueryable();
        var alDoce = new EstadoFinancieroQuery(EstadoFinanciero.EstadoResultados, 2026, 12);
        var alTrece = new EstadoFinancieroQuery(EstadoFinanciero.EstadoResultados, 2026, 13);

        H.MovimientosDeResultados(movs, alDoce, 2026).Select(m => m.PeriodoNumero).Should().Equal(12);
        H.MovimientosDeResultados(movs, alTrece, 2026).Select(m => m.PeriodoNumero).Should().Equal(12, 13);
        H.AvisosDelPeriodo(alDoce).Should().BeEmpty();
        H.AvisosDelPeriodo(alTrece).Should().ContainSingle().Which.Should().Contain("periodo 12");
    }
}
