using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Domain.Ports;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.Compartido.UnitTests.Adjuntos;

namespace Millet.Compartido.UnitTests;

public sealed class TiposCambioCatalogoRangoTests : IDisposable
{
    private readonly CompartidoDbContext _db;

    public TiposCambioCatalogoRangoTests()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CompartidoDbContext(options, new FakeEmpresa());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RangoEnMxnAsync_DevuelveTiposDeCambioEnRango_ParaMonedasActivas()
    {
        // Arrange
        var usd = new Moneda(Guid.NewGuid(), "USD", "Dólar estadounidense", 2, activa: true);
        var eur = new Moneda(Guid.NewGuid(), "EUR", "Euro", 2, activa: true);
        var cad = new Moneda(Guid.NewGuid(), "CAD", "Dólar canadiense", 2, activa: true);
        var jpyInactiva = new Moneda(Guid.NewGuid(), "JPY", "Yen japonés", 0, activa: false);

        _db.Monedas.AddRange(usd, eur, cad, jpyInactiva);

        var d1 = new DateOnly(2026, 11, 1);
        var d2 = new DateOnly(2026, 11, 2);
        var d3 = new DateOnly(2026, 11, 3);

        _db.TiposCambio.AddRange(
            new TipoCambio(Guid.NewGuid(), usd.Id, d1, 19.50m, OrigenTipoCambio.Manual),
            new TipoCambio(Guid.NewGuid(), usd.Id, d2, 19.55m, OrigenTipoCambio.Banxico),
            new TipoCambio(Guid.NewGuid(), usd.Id, d3, 19.60m, OrigenTipoCambio.Banxico),
            new TipoCambio(Guid.NewGuid(), eur.Id, d1, 21.10m, OrigenTipoCambio.Banxico),
            new TipoCambio(Guid.NewGuid(), eur.Id, d2, 21.20m, OrigenTipoCambio.DOF),
            new TipoCambio(Guid.NewGuid(), cad.Id, d1, 14.10m, OrigenTipoCambio.Manual),
            new TipoCambio(Guid.NewGuid(), jpyInactiva.Id, d1, 0.13m, OrigenTipoCambio.Banxico)
        );

        await _db.SaveChangesAsync();

        // Act - Consultar rango d1 a d2 para USD y EUR
        var resultado = await TiposCambioCatalogo.RangoEnMxnAsync(
            _db, ["USD", "EUR"], d1, d2, CancellationToken.None);

        // Assert
        Assert.Equal(4, resultado.Count);

        Assert.Contains(resultado, r => r.Moneda == "USD" && r.Fecha == d1 && r.ValorEnMxn == 19.50m && r.Origen == "Manual");
        Assert.Contains(resultado, r => r.Moneda == "USD" && r.Fecha == d2 && r.ValorEnMxn == 19.55m && r.Origen == "Banxico");
        Assert.Contains(resultado, r => r.Moneda == "EUR" && r.Fecha == d1 && r.ValorEnMxn == 21.10m && r.Origen == "Banxico");
        Assert.Contains(resultado, r => r.Moneda == "EUR" && r.Fecha == d2 && r.ValorEnMxn == 21.20m && r.Origen == "DOF");

        // Verifica que CAD (no solicitado), USD d3 (fuera de rango) y JPY (inactiva) no estén
        Assert.DoesNotContain(resultado, r => r.Moneda == "CAD");
        Assert.DoesNotContain(resultado, r => r.Fecha == d3);
        Assert.DoesNotContain(resultado, r => r.Moneda == "JPY");
    }

    [Fact]
    public async Task RangoEnMxnAsync_IgnoraMonedasInactivas()
    {
        // Arrange
        var usdInactivo = new Moneda(Guid.NewGuid(), "USD", "Dólar", 2, activa: false);
        _db.Monedas.Add(usdInactivo);
        _db.TiposCambio.Add(new TipoCambio(Guid.NewGuid(), usdInactivo.Id, new DateOnly(2026, 11, 1), 20.0m, OrigenTipoCambio.Manual));
        await _db.SaveChangesAsync();

        // Act
        var resultado = await TiposCambioCatalogo.RangoEnMxnAsync(
            _db, ["USD"], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 1), CancellationToken.None);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task RangoEnMxnAsync_NormalizaCodigosMonedaYEspacios()
    {
        // Arrange
        var usd = new Moneda(Guid.NewGuid(), "USD", "Dólar", 2, activa: true);
        _db.Monedas.Add(usd);
        var fecha = new DateOnly(2026, 11, 1);
        _db.TiposCambio.Add(new TipoCambio(Guid.NewGuid(), usd.Id, fecha, 20.0m, OrigenTipoCambio.Banxico));
        await _db.SaveChangesAsync();

        // Act
        var resultado = await TiposCambioCatalogo.RangoEnMxnAsync(
            _db, ["  usd  "], fecha, fecha, CancellationToken.None);

        // Assert
        Assert.Single(resultado);
        Assert.Equal("USD", resultado[0].Moneda);
    }

    [Fact]
    public async Task RangoEnMxnAsync_ListaMonedasVacia_RetornaColeccionVacia()
    {
        // Act
        var resultado = await TiposCambioCatalogo.RangoEnMxnAsync(
            _db, [], new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 2), CancellationToken.None);

        // Assert
        Assert.Empty(resultado);
    }

    [Fact]
    public async Task TipoCambioReadAdapter_ObtenerRangoTipoCambioAsync_DelegaCorrectamente()
    {
        // Arrange
        var usd = new Moneda(Guid.NewGuid(), "USD", "Dólar", 2, activa: true);
        _db.Monedas.Add(usd);
        var fecha = new DateOnly(2026, 11, 1);
        _db.TiposCambio.Add(new TipoCambio(Guid.NewGuid(), usd.Id, fecha, 19.80m, OrigenTipoCambio.Banxico));
        await _db.SaveChangesAsync();

        var adapter = new TipoCambioReadAdapter(_db);

        // Act
        var resultado = await adapter.ObtenerRangoTipoCambioAsync(["USD"], fecha, fecha, CancellationToken.None);

        // Assert
        Assert.Single(resultado);
        Assert.Equal("USD", resultado[0].Moneda);
        Assert.Equal(19.80m, resultado[0].ValorEnMxn);
        Assert.Equal("Banxico", resultado[0].Origen);
    }
}
