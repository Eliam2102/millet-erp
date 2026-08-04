using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Domain.Ports.Almacen;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Workstream almacén-por-línea PR1: el cubrimiento de RQ dejó de consultar
/// stock por <c>AlmacenDestinoId</c> y ahora consulta el rollup por
/// <b>sucursal</b>. Verifica que el puerto de Compras
/// <see cref="IConsultarStockPort"/> (adapter real, delegando en
/// <c>ConsultarDisponibilidadPorSucursalAsync</c>) suma el stock de TODOS los
/// almacenes de la sucursal.
///
/// <para>Tres garantías con datos SEMBRADOS por el test (robusto, no depende del
/// seed de millet_dev): (1) rollup con 2 almacenes activos de la misma sucursal
/// = suma de ambos — el caso que hoy no ocurre pero deja correcto el sistema;
/// (2) no-regresión con 1 solo almacén = el saldo de ese almacén; (3) sucursal
/// sin stock = 0 (cae a compra en la bifurcación).</para>
/// </summary>
public class CubrimientoStockPorSucursalRollupTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CubrimientoStockPorSucursalRollupTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Rollup_suma_stock_de_dos_almacenes_de_la_misma_sucursal()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var puerto = scope.ServiceProvider.GetRequiredService<IConsultarStockPort>();

        var sucursalId = Guid.NewGuid();
        var almacenA = Guid.NewGuid();
        var almacenB = Guid.NewGuid();
        var subA = Guid.NewGuid();
        var subB = Guid.NewGuid();
        var ubicA = Guid.NewGuid();
        var ubicB = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var claveBase = $"S{Guid.NewGuid():N}".Substring(0, 10);

        try
        {
            // Una sucursal con DOS almacenes activos, cada uno con su sub-almacén,
            // ÚNICA y un saldo del mismo artículo: almacenA → 10, almacenB → 20.
            await SembrarAlmacenConSaldoAsync(db, sucursalId, almacenA, subA, ubicA, claveBase + "A", articuloId, cantidad: 10m);
            await SembrarAlmacenConSaldoAsync(db, sucursalId, almacenB, subB, ubicB, claveBase + "B", articuloId, cantidad: 20m);

            var disp = await puerto.ConsultarPorSucursalAsync(sucursalId, articuloId, CancellationToken.None);

            Assert.Equal(30m, disp.OnHand);       // 10 + 20 (rollup de ambos almacenes)
            Assert.Equal(30m, disp.Disponible);   // disponible == físico (PR4: sin reservas)
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id IN ({subA}, {subB})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id IN ({ubicA}, {ubicB})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id IN ({subA}, {subB})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id IN ({almacenA}, {almacenB})");
        }
    }

    [Fact]
    public async Task Rollup_con_un_solo_almacen_devuelve_su_saldo_sin_regresion()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var puerto = scope.ServiceProvider.GetRequiredService<IConsultarStockPort>();

        var sucursalId = Guid.NewGuid();
        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var ubicId = Guid.NewGuid();
        var articuloId = Guid.NewGuid();
        var clave = $"S{Guid.NewGuid():N}".Substring(0, 11);

        try
        {
            await SembrarAlmacenConSaldoAsync(db, sucursalId, almacenId, subId, ubicId, clave, articuloId, cantidad: 42m);

            var disp = await puerto.ConsultarPorSucursalAsync(sucursalId, articuloId, CancellationToken.None);

            // 1 almacén activo por sucursal (el caso real de MILLET hoy): el rollup
            // por sucursal == el saldo del único almacén → sin regresión de comportamiento.
            Assert.Equal(42m, disp.OnHand);
            Assert.Equal(42m, disp.Disponible);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE sub_almacen_id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    [Fact]
    public async Task Rollup_sin_stock_en_la_sucursal_devuelve_cero()
    {
        using var scope = _factory.Services.CreateScope();
        var puerto = scope.ServiceProvider.GetRequiredService<IConsultarStockPort>();

        // Sucursal y artículo inexistentes (sin fila de saldo) → 0 → todo cae a compra.
        var disp = await puerto.ConsultarPorSucursalAsync(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0m, disp.OnHand);
        Assert.Equal(0m, disp.Disponible);
    }

    private static async Task SembrarAlmacenConSaldoAsync(
        AlmacenDbContext db, Guid sucursalId, Guid almacenId, Guid subId, Guid ubicId,
        string clave, Guid articuloId, decimal cantidad)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacenId}, {clave}, 'Rollup sucursal test', {sucursalId}, 0, 0, NOW(), NOW())");
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
}
