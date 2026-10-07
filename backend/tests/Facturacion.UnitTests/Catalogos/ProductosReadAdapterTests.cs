using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Facturacion.Infrastructure.DatosMaestros;
using Millet.Facturacion.UnitTests.TestDoubles;

namespace Millet.Facturacion.UnitTests.Catalogos;

/// <summary>ADM-07: producto dado de baja no sale para alta de líneas nuevas, pero la lectura histórica por id lo devuelve.</summary>
public class ProductosReadAdapterTests
{
    [Fact]
    public async Task Inactivo_no_aparece_en_busqueda_pero_se_lee_por_id_y_referencia()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"prod-read-{Guid.NewGuid():N}").Options;
        using var db = new CompartidoDbContext(options, new FakeEmpresaContext(null, isBypassed: true));
        var activo = new ProductoAw(Guid.NewGuid(), "DEMO-A", "ACTIVO", "M2");
        var baja = new ProductoAw(Guid.NewGuid(), "DEMO-B", "BAJA", "M2");
        baja.DarDeBaja(DateTime.UtcNow);
        db.ProductosAw.AddRange(activo, baja);
        await db.SaveChangesAsync();
        var adapter = new ProductosReadAdapter(db);

        var busqueda = await adapter.BuscarAsync("DEMO", null, 10, CancellationToken.None);

        busqueda.Should().ContainSingle().Which.ReferenciaExterna.Should().Be("DEMO-A");
        (await adapter.ObtenerAsync(baja.Id, CancellationToken.None)).Should().NotBeNull();
        (await adapter.ResolverPorReferenciaAsync("DEMO-B", CancellationToken.None)).Should().NotBeNull();
    }
}
