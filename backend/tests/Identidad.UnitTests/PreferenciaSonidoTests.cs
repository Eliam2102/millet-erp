using Millet.Identidad.Domain;

namespace Millet.Identidad.UnitTests;

public class PreferenciaSonidoTests
{
    [Fact]
    public void Sonido_opcional_empieza_silenciado_y_puede_desactivarse()
    {
        var p = new UsuarioPreferencia(Guid.NewGuid(), Guid.NewGuid());
        Assert.False(p.SonidoAvisosHabilitado);
        p.CambiarSonidoAvisos(true);
        Assert.True(p.SonidoAvisosHabilitado);
        p.CambiarSonidoAvisos(false);
        Assert.False(p.SonidoAvisosHabilitado);
    }
}
