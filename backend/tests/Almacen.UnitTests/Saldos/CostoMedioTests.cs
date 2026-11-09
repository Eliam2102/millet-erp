using FluentAssertions;
using Millet.Almacen.Domain.Saldos;

namespace Millet.Almacen.UnitTests.Saldos;

public class CostoMedioTests
{
    [Fact]
    public void Primera_compra_es_el_costo_de_la_compra() =>
        CostoMedio.Calcular(0, 0, 10, 100m).Should().Be(100m);

    [Fact]
    public void Segunda_compra_a_otro_costo_pondera() =>
        CostoMedio.Calcular(10, 100m, 30, 140m).Should().Be(130m);

    [Fact]
    public void Existencia_cero_conserva_el_ultimo_promedio()
    {
        var (ex, prom) = CostoMedio.Vigente(new[] { (0m, 90m, DateTimeOffset.Parse("2026-11-01")), (0m, 120m, DateTimeOffset.Parse("2026-11-05")) });
        ex.Should().Be(0); prom.Should().Be(120m);
        CostoMedio.Calcular(ex, prom!.Value, 0, 0).Should().Be(120m);
    }

    [Fact]
    public void Sin_saldos_no_hay_promedio() => CostoMedio.Vigente(Array.Empty<(decimal, decimal, DateTimeOffset)>()).Promedio.Should().BeNull();

    [Fact]
    public void Compra_en_usd_se_convierte_a_mxn_con_el_tc_de_la_compra()
    {
        var mxn = CostoMedio.AMxn(10m, 18.5m);
        mxn.Should().Be(185m);
        CostoMedio.Calcular(10, 100m, 10, mxn).Should().Be(142.5m);
    }

    [Fact]
    public void Tipo_cambio_invalido_falla() =>
        FluentActions.Invoking(() => CostoMedio.AMxn(1m, 0m)).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void Agrupar_por_linea_o_por_sucursal_cambia_la_existencia_base()
    {
        var f = DateTimeOffset.Parse("2026-11-01");
        var hegla1 = (10m, 100m, f); var hegla2 = (30m, 200m, f);
        // por línea (almacén): solo HEGLA 1
        var porLinea = CostoMedio.Vigente(new[] { hegla1 });
        CostoMedio.Calcular(porLinea.Existencia, porLinea.Promedio!.Value, 10, 150m).Should().Be(125m);
        // por sucursal: las dos líneas juntas
        var porSuc = CostoMedio.Vigente(new[] { hegla1, hegla2 });
        porSuc.Promedio.Should().Be(175m);
        CostoMedio.Calcular(porSuc.Existencia, porSuc.Promedio!.Value, 40, 150m).Should().Be(162.5m);
    }
}
