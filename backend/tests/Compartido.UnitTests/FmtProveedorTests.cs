using Microsoft.EntityFrameworkCore;
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
        proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
        Assert.Null(proveedor.TaxId);
    }

    [Fact]
    public async Task Reclasificacion_LimpiaTaxIdYLoRegistraEnBitacora()
    {
        var empresa = new Millet.Compartido.UnitTests.Adjuntos.FakeEmpresa();
        await using var db = new Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        await using var core = new Millet.SharedKernel.Infrastructure.Persistence.CoreDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Millet.SharedKernel.Infrastructure.Persistence.CoreDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var proveedor = Crear();
        proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Extranjero, null, null);
        proveedor.ActualizarDomicilioFiscal(taxId: "DEMO-123");
        db.Proveedores.Add(proveedor);
        db.RegimenesFiscales.Add(new RegimenFiscal(Guid.NewGuid(), "601", "Régimen ficticio", false));
        await db.SaveChangesAsync();
        var audit = new Millet.SharedKernel.Infrastructure.Persistence.AuditLogWriter(core,
            new Millet.Compartido.UnitTests.Adjuntos.FakeClock(), new Millet.SharedKernel.Infrastructure.AuditCorrelationContext());
        var handler = new Millet.Compartido.Application.Catalogos.Proveedores.ActualizarClasificacionFiscalProveedorHandler(
            db, new Millet.Compartido.UnitTests.Adjuntos.FakeUser(), empresa, audit);
        await handler.Handle(new(proveedor.Id, OrigenProveedor.Nacional, "601", null), default);
        Assert.Null(proveedor.TaxId);
        using var cambios = System.Text.Json.JsonDocument.Parse(Assert.Single(core.AuditLog).Cambios);
        Assert.Equal("DEMO-123", cambios.RootElement.GetProperty("antes").GetProperty("TaxId").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cambios.RootElement.GetProperty("despues").GetProperty("TaxId").ValueKind);
    }

    [Fact]
    public void Validadores_RechazanLongitudesYTelefono()
    {
        var crear = new CrearProveedorCommand("DEMO", "Ficticio", "DEM010101AAA", TipoPersonaProveedor.Moral, null, null, null, null, null, TaxId: "ABC");
        Assert.Contains(new CrearProveedorValidator().Validate(crear).Errors, e => e.ErrorCode == "PROVEEDOR_TAXID_SOLO_EXTRANJERO");
        Assert.True(new CrearProveedorValidator().Validate(crear with { Origen = OrigenProveedor.Extranjero }).IsValid);
        var largo = new string('x', 41);
        Assert.Contains(new ActualizarProveedorValidator().Validate(new ActualizarProveedorCommand(Guid.NewGuid(), TaxId: largo)).Errors, e => e.ErrorCode == "PROVEEDOR_TAXID_INVALIDO");
        Assert.Contains(new CrearProveedorValidator().Validate(crear with { Origen = OrigenProveedor.Extranjero, TaxId = largo }).Errors, e => e.ErrorCode == "PROVEEDOR_TAXID_INVALIDO");
        var proveedor = Crear();
        proveedor.ActualizarClasificacionFiscal(OrigenProveedor.Extranjero, null, null);
        Assert.Equal("PROVEEDOR_TAXID_INVALIDO", Assert.Throws<BusinessRuleException>(() => proveedor.ActualizarDomicilioFiscal(taxId: largo)).Code);
        Assert.False(new ActualizarProveedorValidator().Validate(new ActualizarProveedorCommand(Guid.NewGuid(), Pais: "MX")).IsValid);
        Assert.False(new CrearEmpleadoValidator().Validate(new CrearEmpleadoCommand(Guid.NewGuid(), Guid.NewGuid(), "DEMO", "Ficticio", Telefono: "abc")).IsValid);
        Assert.False(new ActualizarEmpleadoValidator().Validate(new ActualizarEmpleadoCommand(Guid.NewGuid(), Extension: new string('1', 11))).IsValid);
    }
}
