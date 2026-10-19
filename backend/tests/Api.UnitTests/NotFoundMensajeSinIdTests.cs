using Millet.Api.Web;

namespace Millet.Api.UnitTests;

public sealed class NotFoundMensajeSinIdTests
{
    [Theory]
    [InlineData("No existe rol con id '375c1a2e-0b1d-4f7e-9a53-2c8e5d6f7a80'.", "No existe rol.")]
    [InlineData("No se encontró la devolución '375c1a2e-0b1d-4f7e-9a53-2c8e5d6f7a80'.", "No se encontró la devolución.")]
    [InlineData("No existe el comprobante 375c1a2e-0b1d-4f7e-9a53-2c8e5d6f7a80.", "No existe el comprobante.")]
    [InlineData("No existe artículo con código 'AB-1'.", "No existe artículo con código 'AB-1'.")]
    public void El_detalle_del_404_no_lleva_uuid(string original, string esperado)
        => Assert.Equal(esperado, GlobalExceptionHandler.SinIdentificadores(original));
}
