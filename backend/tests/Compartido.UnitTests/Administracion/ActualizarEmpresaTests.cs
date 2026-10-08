using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Empresas;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Administracion;

public sealed class ActualizarEmpresaTests
{
    private static ActualizarEmpresaCommand Cambio(Guid id, int? version = 0) =>
        new(id, null, null, null, false, CodigoPostal: "97000", VersionEsperada: version);

    [Theory]
    [InlineData("97000", true)]
    [InlineData("1234", false)]
    [InlineData("abcde", false)]
    public void Valida_Codigo_Postal(string cp, bool valido)
    {
        var resultado = new ActualizarEmpresaValidator().Validate(Cambio(Guid.NewGuid()) with { CodigoPostal = cp });
        Assert.Equal(valido, resultado.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Requiere_Version_Valida(int? version)
    {
        Assert.False(new ActualizarEmpresaValidator().Validate(Cambio(Guid.NewGuid(), version)).IsValid);
    }

    [Fact]
    public async Task Edita_Domicilio_Y_CP_Conservando_Rfc()
    {
        await using var db = CrearDb();
        var empresa = EmpresaPrueba();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();

        var response = await new ActualizarEmpresaHandler(db).Handle(
            Cambio(empresa.Id, empresa.Version) with { Calle = "Calle nueva", NumeroInterior = "2" }, CancellationToken.None);

        Assert.Equal("97000", response.CodigoPostal);
        Assert.Equal("Calle nueva", response.Calle);
        Assert.Equal("2", response.NumeroInterior);
        Assert.Equal("MIL010101ABC", response.Rfc);
        Assert.Equal("México", response.Pais);
    }

    [Fact]
    public async Task Version_Obsoleta_No_Modifica_Datos()
    {
        await using var db = CrearDb();
        var empresa = EmpresaPrueba();
        db.Empresas.Add(empresa);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConcurrencyException>(() => new ActualizarEmpresaHandler(db).Handle(
            Cambio(empresa.Id, empresa.Version + 1), CancellationToken.None));
        Assert.Equal("01000", empresa.CodigoPostal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Rechaza_Domicilio_Vacio(string calle)
    {
        Assert.False(new ActualizarEmpresaValidator().Validate(Cambio(Guid.NewGuid()) with { Calle = calle }).IsValid);
    }

    private static CompartidoDbContext CrearDb() => new(
        new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new FakeEmpresa());

    private static Empresa EmpresaPrueba() => new(Guid.NewGuid(), "MIL", "MIL010101ABC", "Millet prueba", "601",
        "Calle", "1", "Centro", "Mérida", "Mérida", "Yucatán", "México", codigoPostal: "01000");
}
