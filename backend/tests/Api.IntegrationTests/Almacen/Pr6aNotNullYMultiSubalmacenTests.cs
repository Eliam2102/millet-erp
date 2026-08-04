using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Almacén-por-línea PR6a · M1. Las dos garantías nuevas del cambio de dato:
/// <list type="number">
///   <item><b>NOT NULL</b>: una línea con <c>ubicacion_id</c> NULL la rechaza el
///   constraint (<c>23502</c>) antes de que corra el trigger — ya no hay
///   fallback a la ÚNICA que la salve.</item>
///   <item><b><c>MOVIMIENTO_MULTI_SUBALMACEN</c></b>: al retirarse la cabecera
///   como fuente del sub-almacén, el trigger valida por-fila que todas las
///   líneas de un movimiento resuelvan al mismo sub. Dos líneas en bins de
///   sub-almacenes distintos abortan la segunda inserción. Este invariante es
///   el que sostiene que los 4 lectores deriven el sub de UNA línea.</item>
/// </list>
///
/// <para>Fixture aislado por GUIDs vía SQL crudo, cleanup en <c>finally</c>,
/// mismo molde que <see cref="TriggerBinExplicitoTests"/>.</para>
/// </summary>
public class Pr6aNotNullYMultiSubalmacenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Pr6aNotNullYMultiSubalmacenTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Linea_con_ubicacion_null_la_rechaza_el_constraint()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var movId = Guid.NewGuid();
        var clave = $"N{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarAlmacenSubAsync(db, almacenId, subId, clave);
            await InsertarMovimientoAsync(db, movId, tipo: 1, subId, empresaId);

            // Línea SIN ubicacion_id → NOT NULL lo rechaza (23502).
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO almacen.lineas_movimiento
                        (id, movimiento_id, posicion, articulo_id, cantidad, unidad_medida,
                         costo_unitario_mxn, monto_total_mxn, moneda_original, version, created_at, updated_at)
                    VALUES
                        ({Guid.NewGuid()}, {movId}, 1, {Guid.NewGuid()}, 1, 'PZA',
                         0, 0, 'MXN', 0, NOW(), NOW())"));

            var msg = ex.Message + " | " + (ex.InnerException?.Message ?? "");
            // Postgres NOT NULL = 23502; el mensaje nombra la columna.
            Assert.Contains("ubicacion_id", msg);
        }
        finally
        {
            // El movimiento quedó sin líneas (el INSERT de la línea falló), así
            // que se borra por id — el join por ubicación no lo alcanzaría.
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.movimientos_inventario WHERE id = {movId}");
            await LimpiarAsync(db, almacenId, subId);
        }
    }

    [Fact]
    public async Task Segunda_linea_en_otro_subalmacen_aborta_con_multi_subalmacen()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var rackA = Guid.NewGuid();
        var rackB = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var movId = Guid.NewGuid();
        var clave = $"M{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarAlmacenSubAsync(db, almacenId, subA, clave + "A");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subB}, {almacenId}, {clave + "B"}, 'Sub B', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackA}, {subA}, 'RACK-A', 'Rack A', 0, false, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackB}, {subB}, 'RACK-B', 'Rack B', 0, false, 0, NOW(), NOW())");

            // Cabecera en subA (aún existe en M1). Entradas a racks reales.
            await InsertarMovimientoAsync(db, movId, tipo: 0, subA, empresaId);

            // Línea 1 → rack de subA: pasa (sin hermanas).
            await InsertarLineaAsync(db, movId, posicion: 1, rackA, articuloId: Guid.NewGuid());

            // Línea 2 → rack de subB: el trigger ve la hermana en subA y aborta.
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                InsertarLineaAsync(db, movId, posicion: 2, rackB, articuloId: Guid.NewGuid()));

            var msg = ex.Message + " | " + (ex.InnerException?.Message ?? "");
            Assert.Contains("MOVIMIENTO_MULTI_SUBALMACEN", msg);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.movimientos_inventario WHERE id = {movId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id IN ({rackA}, {rackB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE id IN ({rackA}, {rackB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id IN ({subA}, {subB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    [Fact]
    public async Task Salida_segunda_linea_en_otro_subalmacen_aborta_con_multi_subalmacen()
    {
        // Salida-por-línea C2: el check MOVIMIENTO_MULTI_SUBALMACEN NO está
        // gateado por v_es_entrada, así que también protege las SALIDAS (tipo 1).
        // El test existente arriba cubre entrada (tipo 0); éste cubre salida.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var rackA = Guid.NewGuid();
        var rackB = Guid.NewGuid();
        var articuloA = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var movId = Guid.NewGuid();
        var clave = $"S{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarAlmacenSubAsync(db, almacenId, subA, clave + "A");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subB}, {almacenId}, {clave + "B"}, 'Sub B', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackA}, {subA}, 'RACK-SA', 'Rack SA', 0, false, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackB}, {subB}, 'RACK-SB', 'Rack SB', 0, false, 0, NOW(), NOW())");
            // La salida DECREMENTA: el rack de la línea 1 necesita existencia
            // (seed SOLO en el rack de la línea 1; la 2 aborta antes de costear).
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({rackA}, {subA}, {articuloA}, 100, 10, NOW())");

            // Movimiento SALIDA (tipo 1). El check multi-sub corre antes del
            // decremento de salida.
            await InsertarMovimientoAsync(db, movId, tipo: 1, subA, empresaId);

            // Línea 1 → rack de subA: pasa (hay saldo, sin hermanas).
            await InsertarLineaAsync(db, movId, posicion: 1, rackA, articuloId: articuloA);

            // Línea 2 → rack de subB: el trigger ve la hermana en subA y aborta.
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                InsertarLineaAsync(db, movId, posicion: 2, rackB, articuloId: Guid.NewGuid()));

            var msg = ex.Message + " | " + (ex.InnerException?.Message ?? "");
            Assert.Contains("MOVIMIENTO_MULTI_SUBALMACEN", msg);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.movimientos_inventario WHERE id = {movId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id IN ({rackA}, {rackB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE id IN ({rackA}, {rackB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id IN ({subA}, {subB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    [Fact]
    public async Task Salida_por_vale_segunda_linea_en_otro_subalmacen_aborta_con_multi_subalmacen()
    {
        // Salida-por-línea (vale): el VALE emite tipo 2 (SalidaPorVale), NO el
        // tipo 1 del RQ. El check MOVIMIENTO_MULTI_SUBALMACEN no está gateado por
        // tipo, así que también protege al vale. Se usa tipo 2 a propósito para
        // probar el tipo REAL del vale (un tipo equivocado pasaría por la razón
        // equivocada).
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var almacenId = Guid.NewGuid();
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var rackA = Guid.NewGuid();
        var rackB = Guid.NewGuid();
        var articuloA = Guid.NewGuid();
        var empresaId = Guid.NewGuid();
        var movId = Guid.NewGuid();
        var clave = $"V{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            await SembrarAlmacenSubAsync(db, almacenId, subA, clave + "A");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({subB}, {almacenId}, {clave + "B"}, 'Sub B vale', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackA}, {subA}, 'RACK-VA', 'Rack VA', 0, false, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({rackB}, {subB}, 'RACK-VB', 'Rack VB', 0, false, 0, NOW(), NOW())");
            // El vale (salida) DECREMENTA: el rack de la línea 1 necesita existencia.
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario
                    (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({rackA}, {subA}, {articuloA}, 100, 10, NOW())");

            // Movimiento SALIDA POR VALE (tipo 2).
            await InsertarMovimientoAsync(db, movId, tipo: 2, subA, empresaId);

            // Línea 1 → rack de subA: pasa (hay saldo, sin hermanas).
            await InsertarLineaAsync(db, movId, posicion: 1, rackA, articuloId: articuloA);

            // Línea 2 → rack de subB: el trigger ve la hermana en subA y aborta.
            var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                InsertarLineaAsync(db, movId, posicion: 2, rackB, articuloId: Guid.NewGuid()));

            var msg = ex.Message + " | " + (ex.InnerException?.Message ?? "");
            Assert.Contains("MOVIMIENTO_MULTI_SUBALMACEN", msg);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.movimientos_inventario WHERE id = {movId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id IN ({rackA}, {rackB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE id IN ({rackA}, {rackB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id IN ({subA}, {subB})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    // ─────────────────────────── Helpers ───────────────────────────

    private static async Task SembrarAlmacenSubAsync(
        AlmacenDbContext db, Guid almacenId, Guid subId, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacenId}, {clave}, 'Test PR6a', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {almacenId}, {clave}, 'Sub test', 0, 0, 0, NOW(), NOW())");
    }

    private static Task<int> InsertarMovimientoAsync(
        AlmacenDbContext db, Guid movId, short tipo, Guid subId, Guid empresaId) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.movimientos_inventario
                (id, tipo, estado, fecha_movimiento, fecha_registro,
                 empresa_id, pendiente_regularizacion, version, created_at, updated_at)
            VALUES
                ({movId}, {tipo}, 2, CURRENT_DATE, NOW(),
                 {empresaId}, false, 0, NOW(), NOW())");

    private static Task<int> InsertarLineaAsync(
        AlmacenDbContext db, Guid movId, int posicion, Guid ubicacionId, Guid articuloId) =>
        db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.lineas_movimiento
                (id, movimiento_id, posicion, articulo_id, cantidad, unidad_medida,
                 costo_unitario_mxn, monto_total_mxn, moneda_original, ubicacion_id, version, created_at, updated_at)
            VALUES
                ({Guid.NewGuid()}, {movId}, {posicion}, {articuloId}, 3, 'PZA',
                 50, 150, 'MXN', {ubicacionId}, 0, NOW(), NOW())");

    private static async Task LimpiarAsync(AlmacenDbContext db, Guid almacenId, Guid subId)
    {
        // PR6a: el sub ya no vive en la cabecera; se borra el movimiento vía la
        // ubicación de sus líneas.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $@"DELETE FROM almacen.movimientos_inventario WHERE id IN (
                 SELECT lm.movimiento_id FROM almacen.lineas_movimiento lm
                 JOIN almacen.ubicaciones u ON u.id = lm.ubicacion_id
                 WHERE u.sub_almacen_id = {subId})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
    }
}
