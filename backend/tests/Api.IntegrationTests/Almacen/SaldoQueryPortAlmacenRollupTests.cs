using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports.Externos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Verifica el reroute cross-module de ADR-0047 PR2: Compras dejó de leer
/// <c>AlmacenDbContext.SaldosInventario</c> directo y ahora consume
/// <see cref="IAlmacenSaldoQueryPort.ConsultarDisponibilidadPorAlmacenAsync"/>.
///
/// <para>Dos garantías, con datos SEMBRADOS por el test (no depende del seed de
/// millet_dev → robusto): (1) el puerto suma correctamente a través de VARIOS
/// sub-almacenes del mismo almacén (rollup); (2) el número que devuelve el puerto
/// es EXACTAMENTE el que el adapter viejo calculaba leyendo saldos directo. Tras
/// PR4 (sin reservas) la disponibilidad = existencia física.</para>
/// </summary>
public class SaldoQueryPortAlmacenRollupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SaldoQueryPortAlmacenRollupTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Puerto_suma_varios_subalmacenes_y_coincide_con_lectura_directa_vieja()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var puerto = scope.ServiceProvider.GetRequiredService<IAlmacenSaldoQueryPort>();

        var almacenId = Guid.NewGuid();
        var sub1 = Guid.NewGuid();
        var sub2 = Guid.NewGuid();
        var ubic1 = Guid.NewGuid();
        var ubic2 = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var clave = $"R{Guid.NewGuid():N}".Substring(0, 12);

        try
        {
            // Almacén con DOS sub-almacenes, cada uno con su ÚNICA y un saldo del
            // mismo artículo: sub1 → 10, sub2 → 20 (sin reservas, PR4).
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({almacenId}, {clave}, 'Rollup test', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");

            await SembrarSubAlmacenConSaldoAsync(db, almacenId, sub1, ubic1, clave + "A", articuloId, cantidad: 10m);
            await SembrarSubAlmacenConSaldoAsync(db, almacenId, sub2, ubic2, clave + "B", articuloId, cantidad: 20m);

            // ── (1) Rollup vía el PUERTO real ──────────────────────────────
            var porPuerto = await puerto.ConsultarDisponibilidadPorAlmacenAsync(
                almacenId, articuloId, CancellationToken.None);

            Assert.Equal(30m, porPuerto.Cantidad);            // 10 + 20
            Assert.Equal(30m, porPuerto.CantidadDisponible);  // disponible == físico (PR4)

            // ── (2) Baseline = lógica del adapter VIEJO (lectura directa) ──
            var (viejoOnHand, viejoDisponible) =
                await LecturaDirectaVieja(db, almacenId, articuloId);

            Assert.Equal(viejoOnHand, porPuerto.Cantidad);
            Assert.Equal(viejoDisponible, porPuerto.CantidadDisponible);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id IN ({sub1}, {sub2})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id IN ({ubic1}, {ubic2})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id IN ({sub1}, {sub2})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    private static async Task SembrarSubAlmacenConSaldoAsync(
        AlmacenDbContext db, Guid almacenId, Guid subId, Guid ubicId, string clave,
        Guid articuloId, decimal cantidad)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {almacenId}, {clave}, 'Sub rollup', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ubicId}, {subId}, 'ÚNICA', 'Única rollup', 0, true, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
            VALUES ({ubicId}, {subId}, {articuloId}, {cantidad}, 100, NOW())");
    }

    // Réplica EXACTA de lo que hacía AlmacenStockReadAdapter antes del reroute:
    // obtener los sub-almacenes del almacén y sumar los saldos del artículo.
    private static async Task<(decimal OnHand, decimal Disponible)> LecturaDirectaVieja(
        AlmacenDbContext db, Guid almacenId, Guid articuloId)
    {
        var subAlmacenIds = await db.SubAlmacenes.AsNoTracking()
            .Where(s => s.AlmacenId == almacenId)
            .Select(s => s.Id)
            .ToListAsync();

        if (subAlmacenIds.Count == 0)
            return (0m, 0m);

        var t = await db.SaldosInventario.AsNoTracking()
            .Where(s => s.ArticuloId == articuloId && subAlmacenIds.Contains(s.SubAlmacenId))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                OnHand = g.Sum(s => (decimal?)s.Cantidad) ?? 0m,
                Disponible = g.Sum(s => (decimal?)s.CantidadDisponible) ?? 0m,
            })
            .FirstOrDefaultAsync();

        return t is null ? (0m, 0m) : (t.OnHand, t.Disponible);
    }
}
