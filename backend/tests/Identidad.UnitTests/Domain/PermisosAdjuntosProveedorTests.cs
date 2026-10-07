using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisosAdjuntosProveedorTests
{
    [Fact]
    public void Permisos_de_adjuntos_de_proveedor_existen_en_el_catalogo_canonico()
    {
        var codigos = PermisosCanonicos.Todos.Select(p => p.Codigo).ToHashSet();

        Assert.Contains("datos_maestros.proveedores.adjuntos-ver", codigos);
        Assert.Contains("datos_maestros.proveedores.adjuntos-subir", codigos);
        Assert.Contains("datos_maestros.proveedores.adjuntos-baja", codigos);
    }
}
