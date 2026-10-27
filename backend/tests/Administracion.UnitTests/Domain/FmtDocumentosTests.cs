using Millet.Administracion.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

public sealed class FmtDocumentosTests
{
    [Fact]
    public void Logo_Asigna_Reemplaza_Quita_Y_Audita()
    {
        var empresa = new Empresa(Guid.NewGuid(), "DEMO", "DEM010101AAA", "Empresa ficticia", "601", "Calle demo", "1", "Demo", "Demo", "Demo", "Demo", "MEX");
        var usuario = Guid.NewGuid();
        var ahora = DateTimeOffset.Parse("2026-10-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        empresa.AsignarLogo("empresas/demo/logo/uno.png", "image/png", usuario, ahora);
        Assert.Equal("image/png", empresa.LogoContentType);
        empresa.AsignarLogo("empresas/demo/logo/dos.jpg", "image/jpeg", usuario, ahora.AddMinutes(1));
        Assert.EndsWith("dos.jpg", empresa.LogoBlobRef);
        Assert.Equal(ahora.AddMinutes(1), empresa.LogoActualizadoEn);
        empresa.QuitarLogo(usuario, ahora.AddMinutes(2));
        Assert.Null(empresa.LogoBlobRef);
        Assert.Null(empresa.LogoContentType);
        Assert.Equal(usuario, empresa.LogoActualizadoPor);
        Assert.Equal(ahora.AddMinutes(2), empresa.LogoActualizadoEn);
    }

    [Theory]
    [InlineData("correo@demo")]
    [InlineData("123456789012345678901")]
    public void TelefonoInvalido_NoModificaContacto(string telefono)
    {
        var empleado = new Empleado(Guid.NewGuid(), Guid.NewGuid(), "DEMO", "Empleado ficticio", telefono: "+52 (999) 123-4567", extension: "123");
        Assert.Throws<BusinessRuleException>(() => empleado.ActualizarDatos(telefono: telefono));
        Assert.Equal("+52 (999) 123-4567", empleado.Telefono);
        empleado.ActualizarDatos(telefono: "", extension: "");
        Assert.Null(empleado.Telefono);
        Assert.Null(empleado.Extension);
    }
}
