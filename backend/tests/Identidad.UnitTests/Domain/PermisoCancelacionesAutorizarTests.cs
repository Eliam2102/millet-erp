using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisoCancelacionesAutorizarTests
{
    [Fact]
    public void Conserva_codigo_e_id_del_permiso_de_autorizar_cancelaciones()
    {
        var permiso = Assert.Single(PermisosCanonicos.Todos, x => x.Codigo == PermisosCanonicos.FacturacionCancelacionesAutorizar);
        Assert.Equal("facturacion.cancelaciones.autorizar", permiso.Codigo);
        Assert.Equal(Guid.Parse("00000009-0007-0000-0000-000000000003"), permiso.Id);
    }
}
