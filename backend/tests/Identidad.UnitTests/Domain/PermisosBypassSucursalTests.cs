using Millet.Administracion.Application.Abstractions;
using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisosBypassSucursalTests
{
    [Fact]
    public void Permisos_de_bypass_del_guard_existen_en_el_catalogo_canonico()
    {
        var codigos = PermisosCanonicos.Todos.Select(p => p.Codigo).ToHashSet();

        Assert.Contains(SucursalScopeGuardPermisos.OrdenesCompraLeerTodas, codigos);
        Assert.Equal(PermisosCanonicos.ComprasOrdenesLeerTodasSucursales, SucursalScopeGuardPermisos.OrdenesCompraLeerTodas);

        Assert.Contains(SucursalScopeGuardPermisos.RequisicionesLeerTodas, codigos);
        Assert.Equal(PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales, SucursalScopeGuardPermisos.RequisicionesLeerTodas);
    }
}
