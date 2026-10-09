using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Infrastructure.Catalogos;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Catalogos;

public sealed class TipoCambioFacturaReadAdapterTests
{
    [Fact]
    public async Task Consulta_solo_TC_de_moneda_y_fecha_solicitadas_sin_usar_ultimo_disponible()
    {
        using var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new FakeEmpresaContext(null, isBypassed: true));
        var usd = new Moneda(Guid.NewGuid(), "USD", "Dólar");
        var eur = new Moneda(Guid.NewGuid(), "EUR", "Euro");
        var fecha = new DateOnly(2026, 9, 30);
        db.Monedas.AddRange(usd, eur);
        db.TiposCambio.AddRange(
            new TipoCambio(Guid.NewGuid(), usd.Id, fecha.AddDays(-1), 19m),
            new TipoCambio(Guid.NewGuid(), usd.Id, fecha, 18.25m),
            new TipoCambio(Guid.NewGuid(), eur.Id, fecha, 21m));
        await db.SaveChangesAsync();
        var adapter = new CompartidoCatalogosSatReadAdapter(db);
        (await adapter.TipoCambioAsync("usd", fecha, default)).Should().Be(18.25m);
        (await adapter.TipoCambioAsync("USD", fecha.AddDays(1), default)).Should().BeNull();
        usd.ActualizarDatos(activa: false);
        await db.SaveChangesAsync();
        (await adapter.TipoCambioAsync("USD", fecha, default)).Should().BeNull();
    }
}
