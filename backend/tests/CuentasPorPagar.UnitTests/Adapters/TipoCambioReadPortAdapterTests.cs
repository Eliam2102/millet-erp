using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.Adapters;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.UnitTests.Adapters;

/// <summary>
/// C1.2: <see cref="TipoCambioReadPortAdapter"/> contra <c>CompartidoDbContext</c> in-memory.
/// Usa solo el TC de la fecha exacta, cruza por MXN y nunca inventa un valor.
/// </summary>
public class TipoCambioReadPortAdapterTests
{
    private static readonly DateOnly Dia = new(2026, 10, 15);

    private static async Task<CompartidoDbContext> NewDbAsync()
    {
        var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"tipos-cambio-{Guid.NewGuid():N}").Options, new BypassedEmpresaContext());
        var usd = new Moneda(Guid.CreateVersion7(), "USD", "Dólar");
        var eur = new Moneda(Guid.CreateVersion7(), "EUR", "Euro");
        var cad = new Moneda(Guid.CreateVersion7(), "CAD", "Dólar canadiense", activa: false);
        db.Monedas.AddRange(usd, eur, cad);
        db.TiposCambio.AddRange(
            new TipoCambio(Guid.CreateVersion7(), usd.Id, Dia, 18.25m),
            new TipoCambio(Guid.CreateVersion7(), usd.Id, Dia.AddDays(-1), 18.10m),
            new TipoCambio(Guid.CreateVersion7(), eur.Id, Dia, 21.90m),
            new TipoCambio(Guid.CreateVersion7(), cad.Id, Dia, 13.30m));
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Usa_el_TC_del_catalogo_de_la_fecha_exacta()
    {
        await using var db = await NewDbAsync();
        var port = new TipoCambioReadPortAdapter(db);

        (await port.ObtenerAsync("USD", "MXN", Dia, default)).Should().Be(18.25m);
        (await port.ObtenerAsync("usd", "mxn", Dia.AddDays(-1), default)).Should().Be(18.10m);
        (await port.ObtenerAsync("MXN", "USD", Dia, default)).Should().Be(Math.Round(1m / 18.25m, 6));
        (await port.ObtenerAsync("EUR", "USD", Dia, default)).Should().Be(Math.Round(21.90m / 18.25m, 6));
        (await port.ObtenerAsync("USD", "USD", Dia.AddYears(5), default)).Should().Be(1m);
    }

    [Fact]
    public async Task Sin_TC_para_la_fecha_o_moneda_inactiva_devuelve_null()
    {
        await using var db = await NewDbAsync();
        var port = new TipoCambioReadPortAdapter(db);

        // No toma el del día anterior ni el siguiente: el consumidor reporta la falta.
        (await port.ObtenerAsync("USD", "MXN", Dia.AddDays(1), default)).Should().BeNull();
        (await port.ObtenerAsync("EUR", "MXN", Dia.AddDays(-1), default)).Should().BeNull();
        (await port.ObtenerAsync("CAD", "MXN", Dia, default)).Should().BeNull();
        (await port.ObtenerAsync("JPY", "MXN", Dia, default)).Should().BeNull();
        (await port.ObtenerAsync("USD", "JPY", Dia, default)).Should().BeNull();
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
