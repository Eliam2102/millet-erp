using Millet.Catalogos.Domain;

namespace Millet.Catalogos.UnitTests;

/// <summary>
/// K10.6: regla de la escalera con los valores de octubre de 2026 publicados por Banxico
/// (columna «Para pagos»). El FIX por fecha de determinación sale de esa misma tabla:
/// el valor para pagos del día X es el FIX del penúltimo día hábil anterior a X.
/// </summary>
public sealed class EscaleraTipoCambioTests
{
    private static DateOnly Oct(int dia) => new(2026, 10, dia);
    private static DateOnly Nov(int dia) => new(2026, 11, dia);

    private static readonly Dictionary<DateOnly, decimal> FixUsd = new()
    {
        [Oct(8)] = 18.1128m, [Oct(9)] = 18.4163m, [Oct(12)] = 18.2542m, [Oct(13)] = 18.3528m,
        [Oct(14)] = 18.4888m, [Oct(15)] = 18.4250m, [Oct(16)] = 18.4930m, [Oct(19)] = 18.4253m,
        [Oct(20)] = 18.4200m, [Oct(21)] = 18.6425m, [Oct(22)] = 18.7005m, [Oct(23)] = 18.6670m,
    };

    private static readonly Dictionary<DateOnly, decimal> Eur = new()
    {
        [Oct(14)] = 20.6618m, [Oct(15)] = 20.8213m, [Oct(16)] = 20.6704m, [Oct(19)] = 20.6451m,
        [Oct(20)] = 20.9402m, [Oct(21)] = 20.9717m,
    };

    private static readonly Dictionary<DateOnly, decimal> Cad = new()
    {
        [Oct(14)] = 12.9955m, [Oct(15)] = 13.0395m, [Oct(16)] = 13.0804m, [Oct(19)] = 13.1216m,
        [Oct(20)] = 13.2503m, [Oct(21)] = 13.3458m,
    };

    private static IReadOnlySet<DateOnly> Habiles => FixUsd.Keys.ToHashSet();

    private static Dictionary<DateOnly, decimal> Escalera(IReadOnlyDictionary<DateOnly, decimal> fix) =>
        EscaleraTipoCambio.Aplicar(fix, Habiles, Oct(23), Oct(16), Oct(24))
            .ToDictionary(a => a.Fecha, a => a.Valor);

    [Fact]
    public void Usd_el_fix_del_lunes_19_rige_el_miercoles_21_y_el_sabado_usa_el_fix_del_jueves()
    {
        var usd = Escalera(FixUsd);

        usd[Oct(16)].Should().Be(18.4888m);
        usd[Oct(17)].Should().Be(18.4250m, "sábado: DOF del viernes 16 = FIX del jueves 15");
        usd[Oct(18)].Should().Be(18.4250m);
        usd[Oct(19)].Should().Be(18.4250m);
        usd[Oct(20)].Should().Be(18.4930m);
        usd[Oct(21)].Should().Be(18.4253m, "FIX del lunes 19, publicado en el DOF del martes 20");
        usd[Oct(22)].Should().Be(18.4200m);
        usd[Oct(23)].Should().Be(18.6425m);
        usd[Oct(24)].Should().Be(18.7005m);
    }

    [Theory]
    [InlineData("EUR", 20.8213, 20.6704, 20.6451, 20.9402, 20.9717)]
    [InlineData("CAD", 13.0395, 13.0804, 13.1216, 13.2503, 13.3458)]
    public void Eur_y_cad_siguen_la_misma_escalera(
        string moneda, double sabADom, double martes, double miercoles, double jueves, double viernes)
    {
        var valores = Escalera(moneda == "EUR" ? Eur : Cad);

        valores[Oct(17)].Should().Be((decimal)sabADom);
        valores[Oct(18)].Should().Be((decimal)sabADom);
        valores[Oct(19)].Should().Be((decimal)sabADom);
        valores[Oct(20)].Should().Be((decimal)martes);
        valores[Oct(21)].Should().Be((decimal)miercoles);
        valores[Oct(22)].Should().Be((decimal)jueves);
        valores[Oct(23)].Should().Be((decimal)viernes);
    }

    [Fact]
    public void Un_festivo_bancario_alarga_la_repeticion_hasta_el_dia_habil_siguiente()
    {
        // Lunes 16-nov-2026 inhábil: no hay FIX ese día.
        var fix = new Dictionary<DateOnly, decimal>
        {
            [Nov(11)] = 18.0000m, [Nov(12)] = 18.1000m, [Nov(13)] = 18.2000m,
            [Nov(17)] = 18.3000m, [Nov(18)] = 18.4000m,
        };

        var valores = EscaleraTipoCambio.Aplicar(fix, fix.Keys.ToHashSet(), Nov(18), Nov(14), Nov(19))
            .ToDictionary(a => a.Fecha);

        valores[Nov(14)].Valor.Should().Be(18.1000m);
        valores[Nov(16)].Valor.Should().Be(18.1000m, "el festivo repite el último valor");
        valores[Nov(17)].Valor.Should().Be(18.1000m, "el último día hábil antes del martes es el viernes 13");
        valores[Nov(17)].FechaFix.Should().Be(Nov(12));
        valores[Nov(18)].Valor.Should().Be(18.2000m, "DOF del martes 17 = FIX del viernes 13");
        valores[Nov(19)].Valor.Should().Be(18.3000m);
    }

    [Fact]
    public void El_viernes_despues_del_fix_ya_se_conoce_el_fin_de_semana_y_se_calcula_hasta_el_lunes()
    {
        var habiles = FixUsd.Keys.Where(d => d <= Oct(16)).ToHashSet();

        var conocido = EscaleraTipoCambio.CalendarioConocidoHasta(habiles, Oct(16));
        var fechas = EscaleraTipoCambio.Aplicar(FixUsd, habiles, conocido, Oct(17), conocido.AddDays(1));

        conocido.Should().Be(Oct(18));
        fechas.Select(a => a.Fecha).Should().Equal(Oct(17), Oct(18), Oct(19));
        fechas.Should().OnlyContain(a => a.Valor == 18.4250m);
    }

    [Fact]
    public void Antes_del_fix_del_dia_solo_se_calcula_hasta_hoy()
    {
        var habiles = FixUsd.Keys.Where(d => d <= Oct(20)).ToHashSet();

        var conocido = EscaleraTipoCambio.CalendarioConocidoHasta(habiles, Oct(21));
        var fechas = EscaleraTipoCambio.Aplicar(FixUsd, habiles, conocido, Oct(21), conocido.AddDays(1));

        conocido.Should().Be(Oct(20));
        fechas.Should().ContainSingle().Which.Should().Be(new AplicacionTipoCambio(Oct(21), Oct(19), 18.4253m));
    }

    [Fact]
    public void Sin_el_fix_que_corresponde_no_se_inventa_un_valor()
    {
        var sinJueves = FixUsd.Where(kv => kv.Key != Oct(15)).ToDictionary();

        var valores = EscaleraTipoCambio.Aplicar(sinJueves, Habiles, Oct(23), Oct(17), Oct(20))
            .Select(a => a.Fecha);

        valores.Should().Equal(Oct(20));
    }
}
