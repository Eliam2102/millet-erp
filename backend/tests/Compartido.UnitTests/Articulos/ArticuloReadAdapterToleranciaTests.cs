using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Compartido.UnitTests.Articulos;

/// <summary>
/// Verifica que <see cref="ArticuloReadAdapter"/> proyecta la tolerancia real
/// y la categoría del artículo, tanto en lectura unitaria como en batch.
/// </summary>
public sealed class ArticuloReadAdapterToleranciaTests
{
    [Fact]
    public async Task ObtenerYObtenerPorIds_DevuelvenToleranciaYCategoriaReales()
    {
        var empresa = new FakeEmpresaBypass();
        using var db = new CompartidoDbContext(
            new DbContextOptionsBuilder<CompartidoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            empresa);

        var sinTolerancia = new Articulo(Guid.NewGuid(), "ART-N", "Sin tolerancia", "PZA", categoria: "Vidrio");
        var cincoPorciento = new Articulo(Guid.NewGuid(), "ART-5", "Tolerancia 5", "PZA", categoria: "Refacciones");
        cincoPorciento.ActualizarToleranciaRecepcion(5);
        var doceConCinco = new Articulo(Guid.NewGuid(), "ART-12", "Tolerancia 12.5", "PZA", categoria: "Insumos");
        doceConCinco.ActualizarToleranciaRecepcion(12.5m);

        db.Articulos.AddRange(sinTolerancia, cincoPorciento, doceConCinco);
        await db.SaveChangesAsync();

        var adapter = new ArticuloReadAdapter(db, empresa);

        var nula = await adapter.ObtenerAsync(sinTolerancia.Id, default);
        Assert.NotNull(nula);
        Assert.Null(nula!.ToleranciaCantidadPorcentaje);
        Assert.Equal("Vidrio", nula.Categoria);

        var cinco = await adapter.ObtenerAsync(cincoPorciento.Id, default);
        Assert.Equal(5m, cinco!.ToleranciaCantidadPorcentaje);
        Assert.Equal("Refacciones", cinco.Categoria);

        var batch = await adapter.ObtenerPorIdsAsync(
            [sinTolerancia.Id, cincoPorciento.Id, doceConCinco.Id], default);
        Assert.Equal(3, batch.Count);
        Assert.Null(batch[sinTolerancia.Id].ToleranciaCantidadPorcentaje);
        Assert.Equal("Vidrio", batch[sinTolerancia.Id].Categoria);
        Assert.Equal(5m, batch[cincoPorciento.Id].ToleranciaCantidadPorcentaje);
        Assert.Equal("Refacciones", batch[cincoPorciento.Id].Categoria);
        Assert.Equal(12.5m, batch[doceConCinco.Id].ToleranciaCantidadPorcentaje);
        Assert.Equal("Insumos", batch[doceConCinco.Id].Categoria);
    }

    private sealed class FakeEmpresaBypass : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Vacio();

        private sealed class Vacio : IDisposable
        {
            public void Dispose() { }
        }
    }
}
