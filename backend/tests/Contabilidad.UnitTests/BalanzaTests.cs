using Millet.Contabilidad.Application.Reportes;
using Millet.Contabilidad.Domain;
using Xunit;

namespace Millet.Contabilidad.UnitTests;

/// <summary>C1.7: armado de la balanza por nivel y sus totales (la consulta SQL solo agrupa por cuenta).</summary>
public sealed class BalanzaTests
{
    // 1 Activo (título) → 1.1 Bancos (título) → 1.1.1 Banco A, 1.1.2 Banco B; 1.2 Clientes (afectable, nivel 2)
    // 2 Pasivo (título) → 2.1 Proveedores (afectable)
    private static readonly CuentaContable Activo = Cuenta("1", null, 1, TipoCuenta.Titulo);
    private static readonly CuentaContable Bancos = Cuenta("1.1", Activo, 2, TipoCuenta.Titulo);
    private static readonly CuentaContable BancoA = Cuenta("1.1.1", Bancos, 3, TipoCuenta.Afectable);
    private static readonly CuentaContable BancoB = Cuenta("1.1.2", Bancos, 3, TipoCuenta.Afectable);
    private static readonly CuentaContable Clientes = Cuenta("1.2", Activo, 2, TipoCuenta.Afectable);
    private static readonly CuentaContable Pasivo = Cuenta("2", null, 1, TipoCuenta.Titulo, NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable Proveedores = Cuenta("2.1", Pasivo, 2, TipoCuenta.Afectable, NaturalezaCuenta.Acreedora);
    private static readonly CuentaContable Rubro = new(Guid.NewGuid(), "R-1", "Rubro", null, 1, null, TipoCuenta.Afectable, CuentaControl.Ninguna,
        null, null, ClaseCuenta.Rubro);
    private static readonly CuentaContable[] Catalogo = [Activo, Bancos, BancoA, BancoB, Clientes, Pasivo, Proveedores, Rubro];

    // Saldo inicial: Banco A 1,000 contra Proveedores. Periodo: Banco B cargo 500 / Proveedores abono 500; Clientes 200 contra Banco A.
    private static readonly Dictionary<Guid, decimal> Iniciales = new() { [BancoA.Id] = 1_000m, [Proveedores.Id] = -1_000m };
    private static readonly Dictionary<Guid, (decimal, decimal)> Periodo = new()
    {
        [BancoB.Id] = (500m, 0m), [Proveedores.Id] = (0m, 500m), [Clientes.Id] = (200m, 0m), [BancoA.Id] = (0m, 200m),
    };

    [Fact]
    public void Sin_nivel_lista_las_cuentas_con_saldo_o_movimientos_y_cuadra()
    {
        var filas = BalanzaHandler.Calcular(Catalogo, Iniciales, Periodo, null);

        Assert.Equal(["1.1.1", "1.1.2", "1.2", "2.1"], filas.Select(f => f.Codigo));
        var a = filas.Single(f => f.Codigo == "1.1.1");
        Assert.Equal((1_000m, 0m, 200m, 800m, 800m, 0m), (a.SaldoInicial, a.Cargos, a.Abonos, a.SaldoFinal, a.SaldoDeudor, a.SaldoAcreedor));
        var p = filas.Single(f => f.Codigo == "2.1");
        Assert.Equal((-1_000m, -1_500m, 0m, 1_500m), (p.SaldoInicial, p.SaldoFinal, p.SaldoDeudor, p.SaldoAcreedor));

        var t = BalanzaHandler.Totales(filas);
        Assert.Equal((1_000m, 1_000m, 700m, 700m, 1_500m, 1_500m, true),
            (t.SaldoInicialDeudor, t.SaldoInicialAcreedor, t.Cargos, t.Abonos, t.SaldoFinalDeudor, t.SaldoFinalAcreedor, t.Cuadra));
    }

    [Fact]
    public void Nivel_2_acumula_en_el_ancestro_y_conserva_las_hojas_menos_profundas()
    {
        var filas = BalanzaHandler.Calcular(Catalogo, Iniciales, Periodo, 2);

        Assert.Equal(["1.1", "1.2", "2.1"], filas.Select(f => f.Codigo));
        var bancos = filas.Single(f => f.Codigo == "1.1");
        Assert.True(bancos.TieneHijas);
        Assert.Equal((1_000m, 500m, 200m, 1_300m), (bancos.SaldoInicial, bancos.Cargos, bancos.Abonos, bancos.SaldoFinal));
        Assert.True(BalanzaHandler.Totales(filas).Cuadra);
    }

    [Fact]
    public void Nivel_1_da_los_grandes_rubros_sin_mostrar_agrupaciones_de_reporte()
    {
        var filas = BalanzaHandler.Calcular(Catalogo, Iniciales, Periodo, 1);

        Assert.Equal(["1", "2"], filas.Select(f => f.Codigo));
        Assert.Equal(1_500m, filas.Single(f => f.Codigo == "1").SaldoFinal);
        Assert.Equal(-1_500m, filas.Single(f => f.Codigo == "2").SaldoFinal);
    }

    [Fact]
    public void Nivel_mas_profundo_que_el_catalogo_deja_cada_cuenta_en_su_lugar_y_lista_las_que_no_se_movieron()
    {
        var filas = BalanzaHandler.Calcular(Catalogo, new Dictionary<Guid, decimal>(), new Dictionary<Guid, (decimal, decimal)>
        {
            [BancoA.Id] = (100m, 0m), [Proveedores.Id] = (0m, 100m),
        }, 4);

        Assert.Equal(["1.1.1", "1.1.2", "1.2", "2.1"], filas.Select(f => f.Codigo));
        Assert.Equal(0m, filas.Single(f => f.Codigo == "1.1.2").SaldoFinal);
    }

    [Fact]
    public void Una_balanza_descuadrada_lo_dice()
    {
        var filas = BalanzaHandler.Calcular(Catalogo, new Dictionary<Guid, decimal>(),
            new Dictionary<Guid, (decimal, decimal)> { [BancoA.Id] = (100m, 0m), [Proveedores.Id] = (0m, 99m) }, null);

        Assert.False(BalanzaHandler.Totales(filas).Cuadra);
    }

    private static CuentaContable Cuenta(string codigo, CuentaContable? padre, int nivel, TipoCuenta tipo,
        NaturalezaCuenta naturaleza = NaturalezaCuenta.Deudora) =>
        new(Guid.NewGuid(), codigo, $"Cuenta {codigo}", padre?.Id, nivel, naturaleza, tipo, CuentaControl.Ninguna, null, null);
}
