using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Almacen.Application.Asignaciones;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración de la asignación artículo→ubicación (OITW, ADR-0047 PR3):
/// asignar crea la fila-en-0, idempotencia (no pisa una fila con saldo previo de
/// un movimiento), desasignar bloqueado por saldo&gt;0 (guardrail EN_USO) y
/// desasignar OK borra la fila-en-0. (Tras PR C la asignación es pura relación
/// artículo↔ubicación; ya no hay niveles ni editar-política.)
///
/// <para>Nivel BD (toca saldos + convive con el trigger). Siembra un fixture
/// aislado por GUIDs y sustituye <see cref="IArticuloReadPort"/> por un fake
/// (cualquier GUID = artículo activo), para no depender del seed de artículos de
/// millet_dev.</para>
/// </summary>
public class AsignacionArticuloUbicacionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _base;

    public AsignacionArticuloUbicacionTests(WebApplicationFactory<Program> factory)
    {
        _base = factory;
    }

    [Fact]
    public async Task Asignar_fila_en_0_idempotencia_desasignar_guardrail_y_editar()
    {
        await using var factory = _base.WithWebHostBuilder(b =>
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IArticuloReadPort>();
                s.AddScoped<IArticuloReadPort, FakeArticuloReadPort>();
            }));

        using var scope = factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var ubicId = Guid.NewGuid();
        var artA = Guid.NewGuid();
        var artB = Guid.NewGuid();
        var artC = Guid.NewGuid();
        var artD = Guid.NewGuid();
        var clave = $"P{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {clave}, 'PR3 test', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subId}, {almacenId}, {clave}, 'Sub PR3', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({ubicId}, {subId}, 'A-01', 'Ubic PR3', 0, false, 0, NOW(), NOW())");

            // ── 1. Asignar artA → crea la fila-en-0 (cantidad 0, sub denormalizado, costo 0) ──
            var rA = await mediator.Send(new AsignarArticuloAUbicacionCommand(ubicId, artA));

            var saldoA = await LeerSaldo(db, ubicId, artA);
            Assert.NotNull(saldoA);
            Assert.Equal(0m, saldoA!.Cantidad);
            Assert.Equal(subId, saldoA.SubAlmacenId);
            Assert.Equal(0m, saldoA.CostoPromedioMxn);
            Assert.Equal(EstatusCatalogo.Activo, rA.Estatus);

            // ── 2. Idempotencia: fila con saldo previo (movimiento) NO se pisa ──
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({ubicId}, {subId}, {artB}, 5, 100, NOW())");

            await mediator.Send(new AsignarArticuloAUbicacionCommand(ubicId, artB));

            var saldoB = await LeerSaldo(db, ubicId, artB);
            Assert.Equal(5m, saldoB!.Cantidad);            // NO pisada a 0
            Assert.Equal(100m, saldoB.CostoPromedioMxn);   // costo preservado

            // ── 3. Desasignar OK (artC, saldo 0): Inactivo + fila-en-0 borrada ──
            var rC = await mediator.Send(new AsignarArticuloAUbicacionCommand(ubicId, artC));
            Assert.NotNull(await LeerSaldo(db, ubicId, artC));

            var rCdes = await mediator.Send(new DesasignarArticuloDeUbicacionCommand(rC.Id));
            Assert.Equal(EstatusCatalogo.Inactivo, rCdes.Estatus);
            Assert.Null(await LeerSaldo(db, ubicId, artC));  // fila-en-0 borrada

            // ── 4. Desasignar BLOQUEADO (artD, saldo>0): guardrail + sin cambios ──
            var rD = await mediator.Send(new AsignarArticuloAUbicacionCommand(ubicId, artD));
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE almacen.saldos_inventario SET cantidad = 3
                 WHERE ubicacion_id = {ubicId} AND articulo_id = {artD}");

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new DesasignarArticuloDeUbicacionCommand(rD.Id)));
            Assert.Equal("ASIGNACION_EN_USO_CON_SALDO", ex.Code);

            var asigD = await db.AsignacionesArticuloUbicacion.AsNoTracking()
                .FirstAsync(a => a.Id == rD.Id);
            Assert.Equal(EstatusCatalogo.Activo, asigD.Estatus);   // sigue activa
            var saldoD = await LeerSaldo(db, ubicId, artD);
            Assert.Equal(3m, saldoD!.Cantidad);                    // saldo sin cambio
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.asignaciones_articulo_ubicacion WHERE ubicacion_id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    private static Task<SaldoInventario?> LeerSaldo(AlmacenDbContext db, Guid ubicId, Guid artId) =>
        db.SaldosInventario.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UbicacionId == ubicId && s.ArticuloId == artId);

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
}
