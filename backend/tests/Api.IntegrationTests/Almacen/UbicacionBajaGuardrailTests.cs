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
/// Tests de integración de la baja de ubicaciones N4 (ADR-0047 PR C7.1): la
/// desactivación tiene DOS guardrails — (1) la ubicación default (ÚNICA) no es
/// desactivable porque es el destino del trigger de saldos
/// (UBICACION_DEFAULT_NO_DESACTIVABLE); (2) una ubicación con saldo &gt; 0 de
/// cualquier artículo no es desactivable (UBICACION_EN_USO_CON_SALDO). La baja
/// limpia pasa a Inactivo (idempotente) y reactivar la vuelve a Activo.
///
/// <para>Nivel BD: siembra un fixture aislado por GUIDs con tres ubicaciones (una
/// real vacía, una real con saldo, una default) y ejercita los handlers vía
/// mediator.</para>
/// </summary>
public class UbicacionBajaGuardrailTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UbicacionBajaGuardrailTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Desactivar_bloquea_default_y_saldo_baja_limpia_y_reactivar()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var ubReal = Guid.NewGuid();      // real, sin saldo → baja limpia
        var ubConSaldo = Guid.NewGuid();  // real, con saldo>0 → guardrail saldo
        var ubDefault = Guid.NewGuid();   // es_default → guardrail default
        var articulo = Guid.NewGuid();
        var fixtureClave = $"C{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {fixtureClave}, 'C7.1 baja', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subId}, {almacenId}, {fixtureClave}, 'Sub baja', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({ubReal}, {subId}, 'R-01', 'Rack vacío', 0, false, 0, NOW(), NOW()),
                       ({ubConSaldo}, {subId}, 'R-02', 'Rack con saldo', 0, false, 0, NOW(), NOW()),
                       ({ubDefault}, {subId}, 'ÚNICA', 'Default del sub', 0, true, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({ubConSaldo}, {subId}, {articulo}, 7, 100, NOW())");

            // ── 1. Desactivar la default → bloqueado ──
            var exDefault = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new DesactivarUbicacionCommand(ubDefault)));
            Assert.Equal("UBICACION_DEFAULT_NO_DESACTIVABLE", exDefault.Code);
            Assert.Equal(EstatusCatalogo.Activo, await LeerEstatus(db, ubDefault));

            // ── 2. Desactivar la que tiene saldo>0 → bloqueado ──
            var exSaldo = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                mediator.Send(new DesactivarUbicacionCommand(ubConSaldo)));
            Assert.Equal("UBICACION_EN_USO_CON_SALDO", exSaldo.Code);
            Assert.Equal(EstatusCatalogo.Activo, await LeerEstatus(db, ubConSaldo));

            // ── 3. Desactivar la real vacía → Inactivo (idempotente) ──
            var r1 = await mediator.Send(new DesactivarUbicacionCommand(ubReal));
            Assert.Equal(EstatusCatalogo.Inactivo, r1.Estatus);
            Assert.Equal(EstatusCatalogo.Inactivo, await LeerEstatus(db, ubReal));

            var r2 = await mediator.Send(new DesactivarUbicacionCommand(ubReal));
            Assert.Equal(EstatusCatalogo.Inactivo, r2.Estatus);   // no-op

            // ── 4. Reactivar la real → Activo ──
            var r3 = await mediator.Send(new ReactivarUbicacionCommand(ubReal));
            Assert.Equal(EstatusCatalogo.Activo, r3.Estatus);
            Assert.Equal(EstatusCatalogo.Activo, await LeerEstatus(db, ubReal));
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE sub_almacen_id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    private static async Task<EstatusCatalogo> LeerEstatus(AlmacenDbContext db, Guid ubicacionId) =>
        (await db.Ubicaciones.AsNoTracking().FirstAsync(u => u.Id == ubicacionId)).Estatus;
}
