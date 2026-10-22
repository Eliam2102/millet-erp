using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.Administracion.Application.Empleados;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests;

public sealed class FmtProveedorTests
{
    private static Proveedor Crear() => new(Guid.NewGuid(), "DEMO", "Proveedor ficticio", "DEM010101AAA", TipoPersonaProveedor.Moral);

    [Fact]
    public void Nacional_RechazaTaxId_ExtranjeroLoConserva()
    {
        var proveedor = Crear();
        proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
        var error = Assert.Throws<BusinessRuleException>(() => proveedor.ActualizarDomicilioFiscal(taxId: "DEMO-123"));
        Assert.Equal("PROVEEDOR_TAXID_SOLO_EXTRANJERO", error.Code);
        Assert.Equal("MEX", proveedor.Pais);
        proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Extranjero, null, null);
        proveedor.ActualizarDomicilioFiscal(calle: "Calle ficticia", pais: "usa", taxId: "DEMO-123");
        Assert.Equal("USA", proveedor.Pais);
        Assert.Equal("DEMO-123", proveedor.TaxId);
        Assert.Throws<BusinessRuleException>(() => proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null));
        proveedor.ActualizarDomicilioFiscal(taxId: "");
        proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
        Assert.Null(proveedor.TaxId);
    }

    [Fact]
    public void Validadores_RechazanLongitudesYTelefono()
    {
        var crear = new CrearProveedorCommand("DEMO", "Ficticio", "DEM010101AAA", TipoPersonaProveedor.Moral, null, null, null, null, null, TaxId: "ABC");
        Assert.Contains(new CrearProveedorValidator().Validate(crear).Errors, e => e.ErrorCode == "PROVEEDOR_TAXID_SOLO_EXTRANJERO");
        Assert.True(new CrearProveedorValidator().Validate(crear with { Origen = OrigenProveedor.Extranjero }).IsValid);
        Assert.False(new ActualizarProveedorValidator().Validate(new(Guid.NewGuid(), TaxId: new string('x', 41))).IsValid);
        Assert.False(new ActualizarProveedorValidator().Validate(new(Guid.NewGuid(), Pais: "MX")).IsValid);
        Assert.False(new CrearEmpleadoValidator().Validate(new(Guid.NewGuid(), Guid.NewGuid(), "DEMO", "Ficticio", Telefono: "abc")).IsValid);
        Assert.False(new ActualizarEmpleadoValidator().Validate(new(Guid.NewGuid(), Extension: new string('1', 11))).IsValid);
    }
}
