using Millet.Almacen.Application.EventListeners;

namespace Millet.Almacen.UnitTests;

/// <summary>
/// M-169: Almacén confirma el desglose por grupo contable que manda Comercio Exterior contra el gasto de importación que de verdad
/// aplicó, y lo pasa a Contabilidad en <c>almacen.expediente.costo-aplicado.v1</c>.
/// </summary>
public sealed class M169GastoPorGrupoTests
{
    private static readonly GastoGrupoAplicado[] Grupos = [new("HONORARIOS_PM", 12000), new("FLETES_MANIOBRAS", 8000)];

    [Fact]
    public void Sin_incrementables_al_inventario_el_desglose_pasa_tal_cual()
    {
        ExpedienteCostoDistribuidoHandler.GastoPorGrupo(Grupos, 20000).Should().Equal(Grupos);
    }

    [Fact]
    public void Evento_viejo_sin_desglose_sigue_sin_desglose()
    {
        ExpedienteCostoDistribuidoHandler.GastoPorGrupo([], 20000).Should().BeEmpty();
    }

    [Fact]
    public void Si_parte_fue_a_inventario_cada_grupo_baja_en_proporcion_y_suma_exacto()
    {
        var r = ExpedienteCostoDistribuidoHandler.GastoPorGrupo([new("HONORARIOS_PM", 100), new("FLETES_MANIOBRAS", 100), new("OTROS", 100)], 100);

        r.Sum(g => g.ImporteMxn).Should().Be(100);
        r.Select(g => g.ImporteMxn).Should().Equal(33.34m, 33.33m, 33.33m);
    }

    [Fact]
    public void Si_todo_fue_a_inventario_no_hay_gasto_por_grupo()
    {
        ExpedienteCostoDistribuidoHandler.GastoPorGrupo(Grupos, 0).Should().BeEmpty();
    }

    [Fact]
    public void Una_reclasificacion_entre_grupos_sin_ajuste_total_pasa_tal_cual()
    {
        GastoGrupoAplicado[] reclasificacion = [new("FLETES_MANIOBRAS", -3000), new("OTROS", 3000)];
        ExpedienteCostoDistribuidoHandler.GastoPorGrupo(reclasificacion, 0).Should().Equal(reclasificacion);
    }
}
