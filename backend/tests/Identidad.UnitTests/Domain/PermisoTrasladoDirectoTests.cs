using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests.Domain;

public sealed class PermisoTrasladoDirectoTests
{
    [Fact]
    public void Conserva_codigo_e_id_y_describe_registro_del_contador()
    {
        var permiso = Assert.Single(PermisosCanonicos.Todos, x => x.Codigo == PermisosCanonicos.ActivosFijosTrasladosGestionar);
        Assert.Equal(Guid.Parse("0000000f-0001-0000-0000-000000000005"), permiso.Id);
        Assert.Equal("Registrar el traslado de un activo a otra ubicación (Contador General)", permiso.Descripcion);
    }
}
