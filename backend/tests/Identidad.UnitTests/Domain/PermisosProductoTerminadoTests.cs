using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisosProductoTerminadoTests
{
    [Fact]
    public void Define_exactamente_cuatro_permisos_con_los_ids_del_contrato()
    {
        var pt = PermisosCanonicos.Todos.Where(p => p.Codigo.StartsWith("producto_terminado.", StringComparison.Ordinal)).ToDictionary(p => p.Codigo, p => p.Id);

        Assert.Equal(4, pt.Count);
        Assert.Equal(Guid.Parse("00000010-0001-0000-0000-000000000001"), pt[PermisosCanonicos.ProductoTerminadoPiezasVer]);
        Assert.Equal(Guid.Parse("00000010-0001-0000-0000-000000000002"), pt[PermisosCanonicos.ProductoTerminadoPiezasVerTodasSucursales]);
        Assert.Equal(Guid.Parse("00000010-0002-0000-0000-000000000001"), pt[PermisosCanonicos.ProductoTerminadoExcepcionesGestionar]);
        Assert.Equal(Guid.Parse("00000010-0002-0000-0000-000000000002"), pt[PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales]);
    }

    [Fact]
    public void Ningun_codigo_termina_en_leer_y_los_ids_no_se_repiten()
    {
        Assert.DoesNotContain(PermisosCanonicos.Todos, p => p.Codigo.StartsWith("producto_terminado.", StringComparison.Ordinal) && p.Codigo.EndsWith(".leer", StringComparison.Ordinal));
        Assert.Equal(PermisosCanonicos.Todos.Count, PermisosCanonicos.Todos.Select(p => p.Id).Distinct().Count());
    }
}
