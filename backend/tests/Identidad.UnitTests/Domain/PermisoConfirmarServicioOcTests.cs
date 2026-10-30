using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisoConfirmarServicioOcTests
{
    [Fact]
    public void Conserva_codigo_e_id_del_permiso_de_conformidad_del_servicio()
    {
        var permiso = Assert.Single(PermisosCanonicos.Todos, x => x.Codigo == PermisosCanonicos.ComprasOrdenesConfirmarServicio);
        Assert.Equal("compras.ordenes.confirmar-servicio", permiso.Codigo);
        Assert.Equal(Guid.Parse("00000003-0003-0000-0000-000000000010"), permiso.Id);
    }
}
