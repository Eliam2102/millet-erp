using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisoToleranciaArticuloRetiradoTests
{
    [Fact]
    public void Permiso_tolerancia_de_articulo_ya_no_esta_en_el_catalogo_canonico()
    {
        var codigos = PermisosCanonicos.Todos.Select(p => p.Codigo).ToHashSet();

        Assert.DoesNotContain("datos_maestros.articulos.tolerancia-editar", codigos);
        // La tolerancia del proveedor (en pesos) se conserva.
        Assert.Contains("datos_maestros.proveedores.tolerancia-editar", codigos);
    }
}
