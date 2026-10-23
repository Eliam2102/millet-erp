using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Reportes;

/// <summary>
/// <see cref="AlmacenRecepcionesOcReadAdapter"/> contra PostgreSQL: recepción + devolución
/// parcial 8.B (línea de OC resuelta vía la línea de recepción de origen), exclusión de otras OC
/// y de movimientos no Registrados, y lista vacía.
/// </summary>
public class AlmacenRecepcionesOcReadAdapterIntegrationTests : IClassFixture<StubsWebApplicationFactory>
{
    private readonly StubsWebApplicationFactory _factory;

    public AlmacenRecepcionesOcReadAdapterIntegrationTests(StubsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Recepcion_y_devolucion_parcial_devuelven_dos_filas_con_signo_y_linea()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

        var empresa = Guid.NewGuid();
        var articulo = Guid.NewGuid();
        var oc = Guid.NewGuid();
        var otraOc = Guid.NewGuid();
        var lineaOc = Guid.NewGuid();
        var (almacenId, subId, ubicId) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var clave = $"R{Guid.NewGuid():N}".Substring(0, 10);
        var movs = new List<Guid>();

        try
        {
            await SembrarBinAsync(db, almacenId, subId, ubicId, clave);

            var recepcion = Movimiento(db, TipoMovimiento.EntradaCompra, empresa, oc, ubicId, articulo, 10m, lineaOc, registrar: true, movs);
            var recepcionLineaId = recepcion.Lineas.Single().Id;
            Movimiento(db, TipoMovimiento.EntradaCompra, empresa, otraOc, ubicId, articulo, 5m, Guid.NewGuid(), registrar: true, movs);
            Movimiento(db, TipoMovimiento.EntradaCompra, empresa, oc, ubicId, articulo, 7m, Guid.NewGuid(), registrar: false, movs);

            var devolucionMov = Movimiento(db, TipoMovimiento.SalidaPorDevolucionAProveedor, empresa, oc, ubicId, articulo, 4m, null, registrar: true, movs);
            var dev = new DevolucionAProveedor(Guid.CreateVersion7(), empresa, Guid.NewGuid(), "Dañado", Guid.NewGuid(),
                recepcionOrigenId: recepcion.Id, ordenCompraOrigenId: oc);
            dev.AgregarLinea(new LineaDevolucionProveedor(Guid.CreateVersion7(), dev.Id, 1, articulo, 4m, "PZA", 10m, recepcionLineaId));
            typeof(DevolucionAProveedor).GetProperty(nameof(DevolucionAProveedor.Estado))!
                .SetValue(dev, EstadoDevolucionProveedor.Autorizada);
            dev.MarcarRegistrada(devolucionMov.Id, "DEV-TEST");
            db.DevolucionesProveedor.Add(dev);
            await db.SaveChangesAsync();

            var adapter = new AlmacenRecepcionesOcReadAdapter(db,
                scope.ServiceProvider.GetRequiredService<Millet.Compras.Infrastructure.ComprasDbContext>(),
                scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.UnidadesMedida.IConversionUnidadPort>(),
                scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>());
            var filas = await adapter.ListarPorOcAsync([oc], CancellationToken.None);

            filas.Should().HaveCount(2);
            var entrada = filas.Single(f => !f.EsDevolucion);
            entrada.MovimientoId.Should().Be(recepcion.Id);
            entrada.LineaOcId.Should().Be(lineaOc);
            entrada.Cantidad.Should().Be(10m);
            entrada.OcId.Should().Be(oc);
            entrada.Folio.Should().NotBeNullOrEmpty();
            var salida = filas.Single(f => f.EsDevolucion);
            salida.MovimientoId.Should().Be(devolucionMov.Id);
            salida.LineaOcId.Should().Be(lineaOc);
            salida.Cantidad.Should().Be(-4m);

            (await adapter.ListarPorOcAsync([], CancellationToken.None)).Should().BeEmpty();
        }
        finally
        {
            var ids = movs.ToArray();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.devoluciones_proveedor WHERE orden_compra_origen_id = {oc}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.movimientos_inventario WHERE id = ANY({ids})");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ubicId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {almacenId}");
        }
    }

    private static int _seq;

    private static MovimientoInventario Movimiento(
        AlmacenDbContext db, TipoMovimiento tipo, Guid empresa, Guid oc, Guid ubicacion, Guid articulo,
        decimal cantidad, Guid? lineaOcId, bool registrar, List<Guid> ids)
    {
        var mov = new MovimientoInventario(Guid.CreateVersion7(), tipo, empresa, new DateOnly(2027, 1, 15));
        typeof(MovimientoInventario).GetProperty(nameof(MovimientoInventario.OcId))!.SetValue(mov, oc);
        mov.AgregarLinea(new LineaMovimiento(
            Guid.CreateVersion7(), mov.Id, 1, articulo, cantidad, "PZA", 10m,
            ubicacionId: ubicacion, lineaOcId: lineaOcId));
        if (registrar)
        {
            mov.Registrar(FolioMovimiento.Construir(tipo, 2099, Interlocked.Increment(ref _seq)), Guid.NewGuid());
        }
        db.Movimientos.Add(mov);
        ids.Add(mov.Id);
        return mov;
    }

    private static async Task SembrarBinAsync(AlmacenDbContext db, Guid almacenId, Guid subId, Guid ubicId, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({almacenId}, {clave}, 'Recep test', {Guid.NewGuid()}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {almacenId}, {clave}, 'Sub recep test', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ubicId}, {subId}, 'RACK-R1', 'Rack recep', 0, false, 0, NOW(), NOW())");
    }
}
