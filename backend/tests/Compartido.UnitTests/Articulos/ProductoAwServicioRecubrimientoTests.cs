using Millet.DatosMaestros.Domain;

namespace Millet.Compartido.UnitTests.Articulos;

/// <summary>PIN-2: marca EsServicioRecubrimiento del producto A+W (dato del ERP, default false).</summary>
public sealed class ProductoAwServicioRecubrimientoTests
{
    private static ProductoAw Nuevo(bool marca = false) =>
        new(Guid.NewGuid(), "REC-1", "RECUBRIMIENTO", "M2", esServicioRecubrimiento: marca);

    [Fact]
    public void Por_omision_es_false() => Assert.False(Nuevo().EsServicioRecubrimiento);

    [Fact]
    public void Constructor_respeta_la_marca() => Assert.True(Nuevo(true).EsServicioRecubrimiento);

    [Fact]
    public void ActualizarDatos_null_no_cambia_y_bool_asigna()
    {
        var p = Nuevo(true);
        p.ActualizarDatos(descripcion: "OTRA");
        Assert.True(p.EsServicioRecubrimiento);

        p.ActualizarDatos(esServicioRecubrimiento: false);
        Assert.False(p.EsServicioRecubrimiento);

        p.ActualizarDatos(esServicioRecubrimiento: true);
        Assert.True(p.EsServicioRecubrimiento);
    }
}
