using Millet.Integraciones.Aw.Infrastructure.ConsumoComponentes;

namespace Millet.Integraciones.Aw.UnitTests.PiezasPt;

/// <summary>A4.8: lector de composición por pieza terminada (solo SELECT) y qué componentes salen de cada fila.</summary>
public sealed class ConsumoComponentesOrigenTests
{
    private static readonly DateTime T = new(2026, 11, 4, 9, 2, 18);

    [Theory][InlineData(true)][InlineData(false)]
    public void Sql_es_solo_lectura_y_filtra_pieza_terminada_en_ensamblado_y_laminacion(bool postgres)
    {
        var sql = postgres ? ConsumoComponentesOrigenAdapter.ConstruirSqlDemo(true) : ConsumoComponentesOrigenAdapter.ConstruirSqlReal(T, (T, "031781876"), 100);
        Assert.StartsWith("SELECT", sql);
        foreach (var prohibida in new[] { "INSERT", "UPDATE", "DELETE", "EXEC", "MERGE", "DROP" })
            Assert.DoesNotContain(prohibida, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b.fertig = 1", sql);
        Assert.Contains("b.bruch_flag = 0", sql);
        Assert.Contains(postgres ? "length(b.etikettnr) = 9" : "LEN(b.etikettnr) = 9", sql);
        Assert.Contains("b.art_nr IN (999970, 999980)", sql);
    }

    [Fact]
    public void Real_va_por_OPENQUERY_a_AWPROD_ERP_y_toma_la_sucursal_del_pedido_en_MILMAIN()
    {
        var sql = ConsumoComponentesOrigenAdapter.ConstruirSqlReal(T, null, 50);
        Assert.Contains("OPENQUERY([AWPROD_ERP]", sql);
        Assert.Contains("alcimdb.dbo.awbar_bearbeit", sql);
        Assert.Contains("alcimdb.dbo.pool_teile", sql);
        Assert.Contains("MILMAIN.SYSADM.BW_AUFTR_KOPF", sql);
        Assert.Contains("OR_AVBEREICH", sql);
        Assert.Contains("TOP (50)", sql);
        Assert.DoesNotContain("@", sql);
    }

    [Fact]
    public void Cursor_con_etiqueta_no_alfanumerica_se_rechaza()
    {
        Assert.Throws<ArgumentException>(() => ConsumoComponentesOrigenAdapter.ConstruirSqlReal(T, (T, "1' OR 1=1"), 10));
    }

    private static FilaConsumoOrigen Fila(int op, bool gas, string? comp, int capas = 1) =>
        new("031781876", T, "17000030", 1, op, gas, gas ? "530000" : "0", 875.51m, 3041.65m, comp, capas, 870m, 3036m, "PERIFERICO");

    [Fact]
    public void Ensamblado_con_gas_y_separador_da_argon_por_la_posicion_y_separador_por_su_medida()
    {
        var p = ConsumoComponentesOrigenAdapter.Expandir(Fila(999970, true, "520042"));
        Assert.Equal(2, p.Count);
        Assert.Equal(("530000", 875.51m), (p[0].ComponenteRef, p[0].AnchoMm!.Value));
        Assert.Equal(("520042", 870m), (p[1].ComponenteRef, p[1].AnchoMm!.Value));
        Assert.All(p, x => Assert.Equal("PERIFERICO", x.ClaveSucursal));
    }

    [Fact]
    public void Laminacion_no_toma_gas_y_conserva_las_capas_del_PVB()
    {
        var p = Assert.Single(ConsumoComponentesOrigenAdapter.Expandir(Fila(999980, true, "419010", capas: 2)));
        Assert.Equal(("419010", 2), (p.ComponenteRef, p.Capas));
    }

    [Fact]
    public void Pieza_sin_gas_ni_componentes_da_la_marca_sin_componente()
    {
        var p = Assert.Single(ConsumoComponentesOrigenAdapter.Expandir(Fila(999970, false, null, capas: 0)));
        Assert.Equal("", p.ComponenteRef);
    }
}
