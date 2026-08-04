using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración de <see cref="ListarUbicacionesQuery"/> (ADR-0047 PR C):
/// lista ubicaciones N4 enriquecidas con clave/nombre del sub-almacén (N3) y del
/// almacén (N2) padres (join in-context), y filtra por sub-almacén. Es el endpoint
/// que puebla el UbicacionSelector del FE de asignación artículo→ubicación.
///
/// <para>Nivel BD; siembra un fixture aislado por GUIDs y limpia al final.</para>
/// </summary>
public class UbicacionesQueryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UbicacionesQueryTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Listar_enriquece_sub_almacen_y_almacen_padres_y_filtra_por_sub_almacen()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almId = Guid.NewGuid();
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var ubA = Guid.NewGuid();
        var ubB = Guid.NewGuid();
        var claveAlm = $"A{Guid.NewGuid():N}".Substring(0, 10);
        var claveSubA = $"SA{Guid.NewGuid():N}".Substring(0, 10);
        var claveSubB = $"SB{Guid.NewGuid():N}".Substring(0, 10);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almId}, {claveAlm}, 'Almacén PR C', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subA}, {almId}, {claveSubA}, 'Sub A', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subB}, {almId}, {claveSubB}, 'Sub B', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({ubA}, {subA}, 'ÚNICA', 'Ubicación única', 0, true, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({ubB}, {subB}, 'ÚNICA', 'Ubicación única', 0, true, 0, NOW(), NOW())");

            // Filtra por sub-almacén A → exactamente 1, enriquecida con los padres.
            var rA = await mediator.Send(new ListarUbicacionesQuery(subA, null, null, 0, 50));
            Assert.Equal(1, rA.Total);
            var itemA = Assert.Single(rA.Items);
            Assert.Equal(ubA, itemA.Id);
            Assert.Equal("ÚNICA", itemA.Clave);
            Assert.True(itemA.EsDefault);
            Assert.Equal(subA, itemA.SubAlmacenId);
            Assert.Equal(claveSubA, itemA.SubAlmacenClave);     // enriquecido N3
            Assert.Equal("Sub A", itemA.SubAlmacenNombre);
            Assert.Equal(claveAlm, itemA.AlmacenClave);         // enriquecido N2
            Assert.Equal("Almacén PR C", itemA.AlmacenNombre);

            // Filtra por sub-almacén B → la otra ÚNICA (distinguible por el padre).
            var rB = await mediator.Send(new ListarUbicacionesQuery(subB, null, null, 0, 50));
            var itemB = Assert.Single(rB.Items);
            Assert.Equal(ubB, itemB.Id);
            Assert.Equal(claveSubB, itemB.SubAlmacenClave);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id IN ({ubA}, {ubB})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id IN ({subA}, {subB})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almId}");
        }
    }
}
