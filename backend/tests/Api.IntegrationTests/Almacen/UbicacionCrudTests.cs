using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración del CRUD de ubicaciones N4 (racks/pasillos, ADR-0047 PR
/// C7.1): crear persiste, clave duplicada por sub-almacén bloquea
/// (UBICACION_CLAVE_DUPLICADA, mismo BusinessRuleException que N2/N3), sub-almacén
/// padre inexistente bloquea (SUBALMACEN_NO_ENCONTRADO) y editar cambia solo
/// clave/nombre (el estatus NO se toca por esta vía).
///
/// <para>Nivel BD: siembra un fixture aislado por GUIDs (almacén + sub-almacén)
/// y ejercita los handlers vía mediator, sin depender del seed de millet_dev.</para>
/// </summary>
public class UbicacionCrudTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UbicacionCrudTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crear_persiste_duplicada_y_padre_inexistente_bloquean_y_editar_cambia_clave_nombre()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var fixtureClave = $"C{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {fixtureClave}, 'C7.1 test', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subId}, {almacenId}, {fixtureClave}, 'Sub C7.1', 0, 0, 0, NOW(), NOW())");

            // ── 1. Crear persiste (Id, SubAlmacenId, Clave) ──
            var creada = await mediator.Send(
                new CrearUbicacionCommand(subId, "HG1-84", "Rack HG1 fila 84", EstatusCatalogo.Activo));

            Assert.Equal(subId, creada.SubAlmacenId);
            Assert.Equal("HG1-84", creada.Clave);

            var enDb = await db.Ubicaciones.AsNoTracking().FirstOrDefaultAsync(u => u.Id == creada.Id);
            Assert.NotNull(enDb);
            Assert.Equal("Rack HG1 fila 84", enDb!.Nombre);
            Assert.Equal(EstatusCatalogo.Activo, enDb.Estatus);
            Assert.False(enDb.EsDefault);   // el CRUD nunca crea una default

            // ── 2. Clave duplicada en el mismo sub-almacén → bloquea ──
            var dup = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new CrearUbicacionCommand(subId, "HG1-84", "Otra", EstatusCatalogo.Activo)));
            Assert.Equal("UBICACION_CLAVE_DUPLICADA", dup.Code);

            // ── 3. Sub-almacén padre inexistente → bloquea ──
            var huerfana = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
                mediator.Send(new CrearUbicacionCommand(Guid.NewGuid(), "X-01", "Huérfana", EstatusCatalogo.Activo)));
            Assert.Equal("SUBALMACEN_NO_ENCONTRADO", huerfana.Code);

            // ── 4. Editar cambia clave/nombre; el estatus queda intacto ──
            await mediator.Send(new EditarUbicacionCommand(creada.Id, "HG1-85", "Rack HG1 fila 85"));

            var editada = await db.Ubicaciones.AsNoTracking().FirstAsync(u => u.Id == creada.Id);
            Assert.Equal("HG1-85", editada.Clave);
            Assert.Equal("Rack HG1 fila 85", editada.Nombre);
            Assert.Equal(EstatusCatalogo.Activo, editada.Estatus);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }
}
