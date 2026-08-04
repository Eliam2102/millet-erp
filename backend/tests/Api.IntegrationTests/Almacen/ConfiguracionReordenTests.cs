using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Almacen.Application.Asignaciones;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración de la configuración de reorden N1/N2 (ADR-0047 PR5.A):
/// crear N2 y N1, multi-config del mismo artículo (Cancún-N1 + Conkal-N2 conviven),
/// exclusión N1⊕N2 dentro de una misma sucursal, entidad inexistente, y
/// asignación-existe (REORDEN_SIN_ASIGNACION). Más un check de que la degradación
/// N4 sigue proyectando bandera/objetivo como informativos.
///
/// <para>Nivel BD. Siembra un fixture aislado por GUIDs; fake de
/// <see cref="IArticuloReadPort"/> y <see cref="ISucursalReadPort"/> (cualquier
/// GUID = activo) para no depender del seed de millet_dev.</para>
/// </summary>
public class ConfiguracionReordenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public ConfiguracionReordenTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
    }

    [Fact]
    public async Task Crear_N1_N2_multiconfig_exclusion_entidad_y_asignacion()
    {
        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IArticuloReadPort>();
                s.AddScoped<IArticuloReadPort, FakeArticuloReadPort>();
                s.RemoveAll<ISucursalReadPort>();
                s.AddScoped<ISucursalReadPort, FakeSucursalReadPort>();
            }));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        // Sucursales (solo ids; no se siembran filas — ISucursalReadPort está fakeado).
        var sucA = Guid.NewGuid(); // "Cancún"
        var sucB = Guid.NewGuid(); // "Conkal"
        // Almacenes: MA bajo sucA; MB1, MB2, M3 bajo sucB.
        var almA = Guid.NewGuid();
        var almB1 = Guid.NewGuid();
        var almB2 = Guid.NewGuid();
        var alm3 = Guid.NewGuid();
        var almInexistente = Guid.NewGuid();
        var subA = Guid.NewGuid(); var subB1 = Guid.NewGuid(); var subB2 = Guid.NewGuid(); var sub3 = Guid.NewGuid();
        var ubA = Guid.NewGuid(); var ubB1 = Guid.NewGuid(); var ubB2 = Guid.NewGuid(); var ub3 = Guid.NewGuid();
        var asigA = Guid.NewGuid();
        var art = Guid.NewGuid();
        string K() => $"T{Guid.NewGuid():N}".Substring(0, 8);

        try
        {
            await SeedAlmacenAsync(db, almA, sucA, K());
            await SeedAlmacenAsync(db, almB1, sucB, K());
            await SeedAlmacenAsync(db, almB2, sucB, K());
            await SeedAlmacenAsync(db, alm3, sucB, K());
            await SeedSubYUbicAsync(db, subA, almA, ubA, K());
            await SeedSubYUbicAsync(db, subB1, almB1, ubB1, K());
            await SeedSubYUbicAsync(db, subB2, almB2, ubB2, K());
            await SeedSubYUbicAsync(db, sub3, alm3, ub3, K());
            // Asignaciones activas de ART: bajo sucA (ubA), y bajo sucB (ubB1, ubB2). NO en ub3.
            await SeedAsignacionAsync(db, asigA, ubA, art);
            await SeedAsignacionAsync(db, Guid.NewGuid(), ubB1, art);
            await SeedAsignacionAsync(db, Guid.NewGuid(), ubB2, art);

            // ── 1. Crear N2(ART, almB1) → ok ──
            var r1 = await mediator.Send(new CrearConfiguracionReordenCommand(
                art, NivelReorden.Almacen, almB1, 5m, 20m, 10m, true, ObjetivoReposicion.Maximo));
            Assert.Equal(EstatusCatalogo.Activo, r1.Estatus);
            Assert.Equal(NivelReorden.Almacen, r1.Nivel);

            // ── 2. Crear N1(ART, sucA) → ok (asignado bajo sucA; sin N2 en sucA) ──
            var r2 = await mediator.Send(new CrearConfiguracionReordenCommand(
                art, NivelReorden.Sucursal, sucA, 3m, 15m, 8m, true, ObjetivoReposicion.Reorden));
            Assert.Equal(NivelReorden.Sucursal, r2.Nivel);

            // ── 3. Crear N2(ART, almB2) → ok (multi-config: N1(sucA)+N2(B1)+N2(B2)) ──
            await mediator.Send(new CrearConfiguracionReordenCommand(
                art, NivelReorden.Almacen, almB2, 1m, 10m, 5m, false, ObjetivoReposicion.Minimo));
            var activas = await db.ConfiguracionesReorden.AsNoTracking()
                .CountAsync(c => c.ArticuloId == art && c.Estatus == EstatusCatalogo.Activo);
            Assert.Equal(3, activas);

            // ── 4. Crear N1(ART, sucB) → conflicto (ya hay N2 activa en almacenes de sucB) ──
            var exExcl = await Assert.ThrowsAsync<ConflictException>(() =>
                mediator.Send(new CrearConfiguracionReordenCommand(
                    art, NivelReorden.Sucursal, sucB, 1m, 10m, 5m, true, ObjetivoReposicion.Maximo)));
            Assert.Equal("REORDEN_NIVEL_EN_CONFLICTO", exExcl.Code);

            // ── 5. Crear N2(ART, alm3) → sin asignación (ART no asignado bajo alm3) ──
            var exSin = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new CrearConfiguracionReordenCommand(
                    art, NivelReorden.Almacen, alm3, 1m, 10m, 5m, true, ObjetivoReposicion.Maximo)));
            Assert.Equal("REORDEN_SIN_ASIGNACION", exSin.Code);

            // ── 6. Crear N2(ART, almacén inexistente) → entidad no encontrada ──
            var exEnt = await Assert.ThrowsAsync<EntityNotFoundException>(() =>
                mediator.Send(new CrearConfiguracionReordenCommand(
                    art, NivelReorden.Almacen, almInexistente, 1m, 10m, 5m, true, ObjetivoReposicion.Maximo)));
            Assert.Equal("REORDEN_ALMACEN_NO_ENCONTRADO", exEnt.Code);

            // ── 7. Tras PR C, la asignación N4 es pura relación artículo↔ubicación
            //    (sin bandera/objetivo/min-máx; el reorden vive solo en N1/N2) ──
            var asig = await mediator.Send(new ObtenerAsignacionPorIdQuery(asigA));
            Assert.NotNull(asig);
            Assert.Equal(art, asig!.ArticuloId);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.configuraciones_reorden WHERE articulo_id = {art}");
            foreach (var ub in new[] { ubA, ubB1, ubB2, ub3 })
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.asignaciones_articulo_ubicacion WHERE ubicacion_id = {ub}");
            foreach (var ub in new[] { ubA, ubB1, ubB2, ub3 })
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ub}");
            foreach (var sub in new[] { subA, subB1, subB2, sub3 })
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {sub}");
            foreach (var alm in new[] { almA, almB1, almB2, alm3 })
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {alm}");
        }
    }

    private static async Task SeedAlmacenAsync(AlmacenDbContext db, Guid id, Guid sucursalId, string clave) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({id}, {clave}, 'RA test', {sucursalId}, 0, 0, NOW(), NOW())");

    private static async Task SeedSubYUbicAsync(AlmacenDbContext db, Guid subId, Guid almacenId, Guid ubicId, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {almacenId}, {clave}, 'Sub RA', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ubicId}, {subId}, {clave}, 'Ubic RA', 0, false, 0, NOW(), NOW())");
    }

    private static async Task SeedAsignacionAsync(
        AlmacenDbContext db, Guid id, Guid ubicId, Guid articuloId) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.asignaciones_articulo_ubicacion
                (id, ubicacion_id, articulo_id, estatus, version, created_at, updated_at)
            VALUES ({id}, {ubicId}, {articuloId}, 0, 0, NOW(), NOW())");

    private sealed class FakeArticuloReadPort : IArticuloReadPort
    {
        public Task<ArticuloLectura?> ObtenerAsync(Guid articuloId, CancellationToken cancellationToken) =>
            Task.FromResult<ArticuloLectura?>(
                new ArticuloLectura(articuloId, "TEST", "Artículo de prueba", "PZA", null, null, EsActivo: true));

        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(
            IReadOnlyCollection<Guid> articuloIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(
                articuloIds.ToDictionary(
                    id => id,
                    id => new ArticuloLectura(id, "TEST", "Artículo de prueba", "PZA", null, null, EsActivo: true)));
    }

    private sealed class FakeSucursalReadPort : ISucursalReadPort
    {
        public Task<SucursalLectura?> ObtenerAsync(Guid sucursalId, CancellationToken cancellationToken) =>
            Task.FromResult<SucursalLectura?>(
                new SucursalLectura(sucursalId, "SUC", "Sucursal de prueba", Guid.NewGuid(), EsActiva: true));
    }
}
