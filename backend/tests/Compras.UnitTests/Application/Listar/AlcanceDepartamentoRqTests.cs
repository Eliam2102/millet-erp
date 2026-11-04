using Millet.Compras.Application.Listar;
using Millet.Compras.Domain;

namespace Millet.Compras.UnitTests.Application.Listar;

/// <summary>F-RQOC-01: sin ver-todos-departamentos solo se ven las RQ del departamento propio o las propias.</summary>
public sealed class AlcanceDepartamentoRqTests
{
    private static readonly Guid Yo = Guid.NewGuid(), MiDepto = Guid.NewGuid(), OtroDepto = Guid.NewGuid();

    private static Requisicion Rq(Guid depto, Guid requisitante, Guid creador) => new(Guid.NewGuid(), Guid.NewGuid(),
        Folio.Parse("MID2026-000001"), 2026, Clasificacion.OrdenCompra, Guid.NewGuid(), depto, null, requisitante, creador,
        Prioridad.Normal, DateTimeOffset.UtcNow);

    [Fact]
    public void Ve_las_de_su_departamento_y_las_que_pidio_o_capturo_pero_no_las_ajenas()
    {
        var visible = new AlcanceDepartamentoRq(Yo, MiDepto).Visible().Compile();
        var otro = Guid.NewGuid();
        Assert.True(visible(Rq(MiDepto, otro, otro)));
        Assert.True(visible(Rq(OtroDepto, Yo, otro)));
        Assert.True(visible(Rq(OtroDepto, otro, Yo)));
        Assert.False(visible(Rq(OtroDepto, otro, otro)));
    }

    [Fact]
    public void Sin_departamento_solo_ve_las_propias()
    {
        var visible = new AlcanceDepartamentoRq(Yo, null).Visible().Compile();
        Assert.False(visible(Rq(MiDepto, Guid.NewGuid(), Guid.NewGuid())));
        Assert.True(visible(Rq(MiDepto, Yo, Yo)));
    }
}
