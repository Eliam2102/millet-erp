using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.UnitTests.Domain;

public class ProveedorTests
{
    private static Proveedor Crear(
        string clave = "PROV-1",
        string razonSocial = "Empresa SA",
        string rfc = "EMP010101AAA",
        TipoPersonaProveedor tipoPersona = TipoPersonaProveedor.Moral,
        EstatusCatalogo estatus = EstatusCatalogo.Activo,
        short? condicionesPagoDias = null) =>
        new(
            id: Guid.CreateVersion7(),
            clave: clave,
            razonSocial: razonSocial,
            rfc: rfc,
            tipoPersona: tipoPersona,
            estatus: estatus,
            condicionesPagoDias: condicionesPagoDias);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var p = Crear();
        Assert.Equal(EstatusCatalogo.Activo, p.Estatus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("THIS-CLAVE-EXCEEDS-20-CHARS")]
    public void Should_Throw_When_ClaveInvalida(string clave)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(clave: clave));
        Assert.Equal("PROVEEDOR_CLAVE_INVALIDA", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_RazonSocialVacia()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(razonSocial: ""));
        Assert.Equal("PROVEEDOR_RAZON_SOCIAL_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData("RFC123")]            // demasiado corto
    [InlineData("RFC12345678901234")] // demasiado largo
    public void Should_Throw_When_RfcLongitudInvalida(string rfc)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(rfc: rfc));
        Assert.Equal("PROVEEDOR_RFC_INVALIDO", ex.Code);
    }

    [Theory]
    [InlineData("EMP010101AAA")]   // 12 chars (moral)
    [InlineData("ABCD010101ABC")]  // 13 chars (física)
    public void Should_Accept_RfcLongitudValida(string rfc)
    {
        var p = Crear(rfc: rfc);
        Assert.Equal(rfc, p.Rfc);
    }

    [Theory]
    [InlineData((short)-1)]
    [InlineData((short)400)]
    public void Should_Throw_When_CondicionesPagoFueraDeRango(short dias)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => Crear(condicionesPagoDias: dias));
        Assert.Equal("PROVEEDOR_CONDICIONES_PAGO_INVALIDAS", ex.Code);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)30)]
    [InlineData((short)365)]
    public void Should_Accept_CondicionesPagoEnRango(short dias)
    {
        var p = Crear(condicionesPagoDias: dias);
        Assert.Equal(dias, p.CondicionesPagoDias);
    }

    [Fact]
    public void Should_Accept_EstatusInactivo()
    {
        var p = Crear(estatus: EstatusCatalogo.Inactivo);
        Assert.Equal(EstatusCatalogo.Inactivo, p.Estatus);
    }
}
