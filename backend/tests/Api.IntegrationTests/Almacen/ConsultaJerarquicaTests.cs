using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Saldos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración de PR6 (ADR-0047): la consulta jerárquica de saldos
/// ("hijos del nodo X" con rollup por nodo). Mismo molde que
/// <see cref="TriggerBinExplicitoTests"/>: fixture aislado por GUIDs vía SQL
/// crudo, cleanup en finally, no toca el seed de millet_dev.
///
/// <para>Fixture canónico: sucursal S (sintética, el read-port no la resuelve
/// → fallback al id) → almacén A → sub1 {ÚNICA 10@100 de X, B1 20@250 de X,
/// B3 con fila-en-0 de Y} + sub2 {B2 5@60 de Y}. Totales: sucursal 35/6300,
/// sub1 30/6000, sub2 5/300.</para>
/// </summary>
public class ConsultaJerarquicaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConsultaJerarquicaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private sealed record Fixture(
        Guid SucursalId, Guid AlmacenId, Guid Sub1, Guid Sub2,
        Guid Unica1, Guid Bin1, Guid Bin3, Guid Bin2,
        Guid ArticuloX, Guid ArticuloY);

    [Fact]
    public async Task Rollup_cuadra_por_nivel_desde_la_raiz()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var fx = await SembrarJerarquiaAsync(db);

        try
        {
            // Raíz → nuestra sucursal con el rollup total. La sucursal es
            // sintética (no existe en compartido) → clave = fallback al id.
            var raiz = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Raiz, null, null, IncluirVacios: false));
            var nodoSucursal = Assert.Single(raiz, n => n.Id == fx.SucursalId);
            Assert.Equal("sucursal", nodoSucursal.Tipo);
            Assert.Equal(35m, nodoSucursal.Cantidad);
            Assert.Equal(6300m, nodoSucursal.ValorInventarioMxn);
            Assert.Equal(fx.SucursalId.ToString(), nodoSucursal.Clave);
            Assert.False(nodoSucursal.EsHoja);

            // Sucursal → su único almacén, mismo rollup.
            var almacenes = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Sucursal, fx.SucursalId, null, false));
            var nodoAlmacen = Assert.Single(almacenes);
            Assert.Equal("almacen", nodoAlmacen.Tipo);
            Assert.Equal(fx.AlmacenId, nodoAlmacen.Id);
            Assert.Equal(35m, nodoAlmacen.Cantidad);
            Assert.Equal(6300m, nodoAlmacen.ValorInventarioMxn);

            // Almacén → 2 sub-almacenes; los subtotales parten el total.
            var subs = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Almacen, fx.AlmacenId, null, false));
            Assert.Equal(2, subs.Count);
            var nodoSub1 = Assert.Single(subs, n => n.Id == fx.Sub1);
            Assert.Equal(30m, nodoSub1.Cantidad);
            Assert.Equal(6000m, nodoSub1.ValorInventarioMxn);
            var nodoSub2 = Assert.Single(subs, n => n.Id == fx.Sub2);
            Assert.Equal(5m, nodoSub2.Cantidad);
            Assert.Equal(300m, nodoSub2.ValorInventarioMxn);
            Assert.All(subs, n => Assert.Equal("subAlmacen", n.Tipo));

            // Sub-almacén → sus ubicaciones (modo ubicación: NO hoja, expanden
            // a artículos). La ÚNICA se distingue por EsDefault.
            var ubicaciones = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.SubAlmacen, fx.Sub1, null, false));
            Assert.Equal(2, ubicaciones.Count);
            var nodoUnica = Assert.Single(ubicaciones, n => n.Id == fx.Unica1);
            Assert.True(nodoUnica.EsDefault);
            Assert.Equal(10m, nodoUnica.Cantidad);
            var nodoBin = Assert.Single(ubicaciones, n => n.Id == fx.Bin1);
            Assert.False(nodoBin.EsDefault);
            Assert.Equal(20m, nodoBin.Cantidad);
            Assert.Equal(5000m, nodoBin.ValorInventarioMxn);
            Assert.All(ubicaciones, n =>
            {
                Assert.Equal("ubicacion", n.Tipo);
                Assert.False(n.EsHoja);
            });
        }
        finally
        {
            await LimpiarAsync(db, fx);
        }
    }

    [Fact]
    public async Task Modo_articulo_filtra_todas_las_ramas_y_la_ubicacion_es_hoja()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var fx = await SembrarJerarquiaAsync(db);

        try
        {
            // Raíz filtrada por X → la sucursal solo suma lo de X (30/6000).
            var raiz = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Raiz, null, fx.ArticuloX, false));
            var nodoSucursal = Assert.Single(raiz, n => n.Id == fx.SucursalId);
            Assert.Equal(30m, nodoSucursal.Cantidad);
            Assert.Equal(6000m, nodoSucursal.ValorInventarioMxn);

            // Sub1 con X → sus 2 ubicaciones, ahora HOJA (artículo fijo).
            var ubicaciones = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.SubAlmacen, fx.Sub1, fx.ArticuloX, false));
            Assert.Equal(2, ubicaciones.Count);
            Assert.All(ubicaciones, n => Assert.True(n.EsHoja));

            // Sub2 con X → vacío (X no vive ahí).
            var vacio = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.SubAlmacen, fx.Sub2, fx.ArticuloX, false));
            Assert.Empty(vacio);
        }
        finally
        {
            await LimpiarAsync(db, fx);
        }
    }

    [Fact]
    public async Task Rack_expande_a_articulos_y_la_fila_en_0_solo_con_el_toggle()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var fx = await SembrarJerarquiaAsync(db);

        try
        {
            // Ubicación → artículos (5º nivel hoja, modo ubicación). El
            // artículo es sintético → clave = fallback al id.
            var articulos = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Ubicacion, fx.Bin1, null, false));
            var nodoArt = Assert.Single(articulos);
            Assert.Equal("articulo", nodoArt.Tipo);
            Assert.Equal(fx.ArticuloX, nodoArt.Id);
            Assert.Equal(20m, nodoArt.Cantidad);
            Assert.Equal(5000m, nodoArt.ValorInventarioMxn);
            Assert.True(nodoArt.EsHoja);
            Assert.Equal(fx.ArticuloX.ToString(), nodoArt.Clave);

            // Sin toggle: B3 (solo fila-en-0 de Y) NO aparece.
            var sinVacios = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.SubAlmacen, fx.Sub1, null, false));
            Assert.Equal(2, sinVacios.Count);
            Assert.DoesNotContain(sinVacios, n => n.Id == fx.Bin3);

            // Con toggle: B3 aparece en 0 — y las sumas NO cambian (los ceros
            // no aportan): 30 en ambos casos.
            var conVacios = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.SubAlmacen, fx.Sub1, null, IncluirVacios: true));
            Assert.Equal(3, conVacios.Count);
            var nodoB3 = Assert.Single(conVacios, n => n.Id == fx.Bin3);
            Assert.Equal(0m, nodoB3.Cantidad);
            Assert.Equal(0m, nodoB3.ValorInventarioMxn);
            Assert.Equal(30m, sinVacios.Sum(n => n.Cantidad));
            Assert.Equal(30m, conVacios.Sum(n => n.Cantidad));

            // El rack vacío expande a su artículo en 0 (el "ver el 0" por bin).
            var articulosB3 = await mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Ubicacion, fx.Bin3, null, IncluirVacios: true));
            var nodoY = Assert.Single(articulosB3);
            Assert.Equal(fx.ArticuloY, nodoY.Id);
            Assert.Equal(0m, nodoY.Cantidad);
        }
        finally
        {
            await LimpiarAsync(db, fx);
        }
    }

    [Fact]
    public async Task Validacion_de_parametros()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Nodo no-raíz sin id.
        var exSinNodo = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Sucursal, null, null, false)));
        Assert.Equal("JERARQUIA_NODO_REQUERIDO", exSinNodo.Code);

        // En modo artículo la ubicación es hoja: expandirla es inválido.
        var exHoja = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            mediator.Send(new ObtenerHijosJerarquiaQuery(
                NivelNodoJerarquia.Ubicacion, Guid.NewGuid(), Guid.NewGuid(), false)));
        Assert.Equal("JERARQUIA_ARTICULO_EN_HOJA", exHoja.Code);
    }

    // ─── Fixture (molde TriggerBinExplicitoTests) ────────────────────────────

    private static async Task<Fixture> SembrarJerarquiaAsync(AlmacenDbContext db)
    {
        var fx = new Fixture(
            SucursalId: Guid.NewGuid(), AlmacenId: Guid.NewGuid(),
            Sub1: Guid.NewGuid(), Sub2: Guid.NewGuid(),
            Unica1: Guid.NewGuid(), Bin1: Guid.NewGuid(),
            Bin3: Guid.NewGuid(), Bin2: Guid.NewGuid(),
            ArticuloX: Guid.NewGuid(), ArticuloY: Guid.NewGuid());
        var clave = $"J{Guid.NewGuid():N}".Substring(0, 12);

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({fx.AlmacenId}, {clave}, 'Test PR6', {fx.SucursalId}, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({fx.Sub1}, {fx.AlmacenId}, {clave + "1"}, 'Sub 1', 0, 0, 0, NOW(), NOW()),
                   ({fx.Sub2}, {fx.AlmacenId}, {clave + "2"}, 'Sub 2', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({fx.Unica1}, {fx.Sub1}, 'ÚNICA', 'Única 1', 0, true,  0, NOW(), NOW()),
                   ({fx.Bin1},   {fx.Sub1}, 'B1',    'Rack B1', 0, false, 0, NOW(), NOW()),
                   ({fx.Bin3},   {fx.Sub1}, 'B3',    'Rack B3 vacío', 0, false, 0, NOW(), NOW()),
                   ({fx.Bin2},   {fx.Sub2}, 'B2',    'Rack B2', 0, false, 0, NOW(), NOW())");
        // Saldos: X en ÚNICA (10@100) y B1 (20@250); Y en B2 (5@60) y la
        // fila-en-0 en B3 (el rack asignado vacío — mismas columnas que el
        // INSERT del handler de asignar). valor_inventario es generada STORED.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad,
                 costo_promedio_mxn, ultima_actualizacion_at, ultimo_movimiento_id)
            VALUES ({fx.Unica1}, {fx.Sub1}, {fx.ArticuloX}, 10, 100, NOW(), NULL),
                   ({fx.Bin1},   {fx.Sub1}, {fx.ArticuloX}, 20, 250, NOW(), NULL),
                   ({fx.Bin2},   {fx.Sub2}, {fx.ArticuloY},  5,  60, NOW(), NULL),
                   ({fx.Bin3},   {fx.Sub1}, {fx.ArticuloY},  0,   0, NOW(), NULL)");
        return fx;
    }

    private static async Task LimpiarAsync(AlmacenDbContext db, Fixture fx)
    {
        foreach (var ubicacionId in new[] { fx.Unica1, fx.Bin1, fx.Bin3, fx.Bin2 })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ubicacionId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ubicacionId}");
        }
        foreach (var subId in new[] { fx.Sub1, fx.Sub2 })
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {subId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {fx.AlmacenId}");
    }
}
