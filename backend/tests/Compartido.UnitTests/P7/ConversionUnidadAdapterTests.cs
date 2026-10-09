using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compartido.UnitTests.P7;
public sealed class ConversionUnidadAdapterTests
{
    [Theory]
    [InlineData("CAJA", "PZA", 2, 24, 24)]
    [InlineData("PZA", "CAJA", 24, 24, 2)]
    public async Task Usa_factor_catalogado_y_conserva_unidades_de_documento(string capturada, string documento, decimal cantidad, decimal enBase, decimal enDocumento)
    {
        await using var db = NuevaDb();
        var pieza = new UnidadMedida(Guid.NewGuid(), "PZA", "Pieza DEMO", DimensionUnidad.Conteo, 1, 0, true);
        var caja = new UnidadMedida(Guid.NewGuid(), "CAJA", "Caja de 12 DEMO", DimensionUnidad.Conteo, 12, 0, false);
        var articulo = new Articulo(Guid.NewGuid(), "P7-DEMO", "Artículo DEMO", "PZA", unidadMedidaId: pieza.Id);
        db.AddRange(pieza, caja, articulo); await db.SaveChangesAsync();
        var resultado = await new ConversionUnidadAdapter(db).ConvertirAsync(articulo.Id, cantidad, capturada, documento, default);
        resultado.CantidadBase.Should().Be(enBase);
        resultado.CantidadDocumento.Should().Be(enDocumento);
        resultado.CantidadCapturada.Should().Be(cantidad);
        resultado.UnidadCapturada.Should().Be(capturada);
        resultado.UnidadBase.Should().Be("PZA");
    }
    [Theory]
    [InlineData("NOEXISTE", "UNIDAD_EQUIVALENCIA_REQUERIDA")]
    [InlineData("KG", "UNIDAD_DIMENSION_INCOMPATIBLE")]
    public async Task Rechaza_unidad_sin_equivalencia_o_de_otra_dimension(string capturada, string codigo)
    {
        await using var db = NuevaDb();
        var pieza = new UnidadMedida(Guid.NewGuid(), "PZA", "Pieza DEMO", DimensionUnidad.Conteo, 1, 0, true);
        var peso = new UnidadMedida(Guid.NewGuid(), "KG", "Kilogramo DEMO", DimensionUnidad.Peso, 1, 3, true);
        var articulo = new Articulo(Guid.NewGuid(), "P7-DEMO", "Artículo DEMO", "PZA", unidadMedidaId: pieza.Id);
        db.AddRange(pieza, peso, articulo); await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => new ConversionUnidadAdapter(db).ConvertirAsync(articulo.Id, 2, capturada, "PZA", default));
        ex.Code.Should().Be(codigo);
    }
    private static CompartidoDbContext NuevaDb() => new(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new Empresa());
    private sealed class Empresa : ICurrentEmpresaContext
    { public Guid? Current => null; public bool IsBypassed => true; public IDisposable Bypass() => new Scope(); private sealed class Scope : IDisposable { public void Dispose() { } } }
}
