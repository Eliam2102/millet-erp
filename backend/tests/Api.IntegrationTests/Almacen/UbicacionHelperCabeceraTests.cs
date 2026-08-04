using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Almacén-por-línea PR4: el helper de cabecera nivel 4
/// (<c>almacen.movimientos_inventario.ubicacion_helper_id</c>) persiste contra
/// Postgres real — valida la migración aditiva, el mapeo EF y la FK a
/// <c>almacen.ubicaciones</c>.
///
/// <para>Dos garantías: (1) sin helper el movimiento persiste con NULL — la
/// semántica de "el almacenista no usó el helper"; (2) con helper persiste el
/// bin elegido. Datos sembrados por el test (no depende del seed de
/// millet_dev).</para>
///
/// <para>Los movimientos se guardan SIN líneas a propósito: el trigger de
/// saldos vive en <c>almacen.lineas_movimiento</c>, así que esta prueba no
/// toca <c>saldos_inventario</c> — aísla la cabecera, que es lo que PR4
/// cambia.</para>
/// </summary>
public class UbicacionHelperCabeceraTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UbicacionHelperCabeceraTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Recepcion_sin_helper_persiste_NULL_y_con_helper_persiste_el_bin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        // MovimientoInventario es IPerteneceAEmpresa: fuera de un request no hay
        // EmpresaContext, así que la escritura va dentro de un Bypass acotado.
        var empresaCtx = scope.ServiceProvider
            .GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        using var bypass = empresaCtx.Bypass();

        var empresa = Guid.NewGuid();
        var almacenId = Guid.NewGuid();
        var subId = Guid.NewGuid();
        var ubicId = Guid.NewGuid();
        var clave = $"H{Guid.NewGuid():N}".Substring(0, 10);
        var movSinHelper = Guid.CreateVersion7();
        var movConHelper = Guid.CreateVersion7();

        try
        {
            await SembrarSubAlmacenConBinAsync(db, almacenId, subId, ubicId, clave);

            // (1) Sin helper → NULL ("no usó el helper").
            db.Movimientos.Add(new MovimientoInventario(
                id: movSinHelper,
                tipo: TipoMovimiento.EntradaCompra,
                empresaId: empresa,                fechaMovimiento: new DateOnly(2027, 1, 15)));

            // (2) Con helper → persiste el bin elegido.
            db.Movimientos.Add(new MovimientoInventario(
                id: movConHelper,
                tipo: TipoMovimiento.EntradaCompra,
                empresaId: empresa,                fechaMovimiento: new DateOnly(2027, 1, 15),
                ubicacionHelperId: ubicId));

            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var sinHelper = await db.Movimientos.AsNoTracking()
                .FirstAsync(m => m.Id == movSinHelper);
            var conHelper = await db.Movimientos.AsNoTracking()
                .FirstAsync(m => m.Id == movConHelper);

            sinHelper.UbicacionHelperId.Should().BeNull();
            conHelper.UbicacionHelperId.Should().Be(ubicId);
            // PR6a: la cabecera ya no lleva sub-almacén (se derivaría de la
            // línea vía la vista). Este test guarda movimientos SIN líneas, así
            // que aquí sólo se verifica el helper, que es lo que PR4 agregó.
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.movimientos_inventario WHERE id IN ({movSinHelper}, {movConHelper})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.ubicaciones WHERE id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    private static async Task SembrarSubAlmacenConBinAsync(
        AlmacenDbContext db, Guid almacenId, Guid subId, Guid ubicId, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacenId}, {clave}, 'Helper test', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {almacenId}, {clave}, 'Sub helper test', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ubicId}, {subId}, 'RACK-H1', 'Rack helper', 0, false, 0, NOW(), NOW())");
    }
}
