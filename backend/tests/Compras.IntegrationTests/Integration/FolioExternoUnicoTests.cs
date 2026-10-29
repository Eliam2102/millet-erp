using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Integration;

public class FolioExternoUnicoTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;
    public FolioExternoUnicoTests(StubsWebApplicationFactory factory) => _factory = factory;

    private static Requisicion Rq(Guid empresa, int n, string folioExterno) => Requisicion.CrearDesdeAlmacenesApp(
        Guid.CreateVersion7(), empresa, Folio.Parse($"MID2026-{n:D6}"), 2026, TestComprasFixtures.SucursalMid,
        TestComprasFixtures.DeptoCompras, null, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow,
        folioExterno, Guid.CreateVersion7(),
        [new LineaAppRequisicion(Guid.CreateVersion7(), 1m, "PZA", Money.Of(1m, "MXN"), null, null, null)]);

    [Fact]
    public async Task FolioExterno_RepetidoEnElMismoOrigen_SeRechaza()
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresa = Guid.CreateVersion7();
        var n = Random.Shared.Next(500000, 999000);
        db.Requisiciones.Add(Rq(empresa, n, "APP-DUP"));
        await db.SaveChangesAsync();

        db.Requisiciones.Add(Rq(empresa, n + 1, "APP-DUP"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
