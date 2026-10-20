using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisoAjustarToleranciaCxpRetiradoTests
{
    [Fact]
    public void Permiso_huerfano_de_CxP_ya_no_esta_en_el_catalogo_canonico()
    {
        var codigos = PermisosCanonicos.Todos.Select(p => p.Codigo).ToHashSet();

        Assert.DoesNotContain("cuentas_por_pagar.proveedores.ajustar-tolerancia", codigos);
        Assert.Contains("datos_maestros.proveedores.tolerancia-editar", codigos);
    }
}
