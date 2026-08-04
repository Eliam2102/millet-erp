using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// Tests de integración del cálculo del faltante del motor de reorden (ADR-0047 PR5.B):
/// faltante = objetivo − existencia física − vivo de origen sistema. Cubre el rollup
/// N2 y N1, el interino (sin RQ de sistema → vivo vacío → faltante = objetivo − física),
/// el filtro origen=sistema (los manuales NO cuentan), el dedup borrador↔OC por línea
/// (no doble conteo), y la prueba del <b>bypass de tenancy</b> (el adapter devuelve el
/// vivo SIN empresa en contexto — como el worker real).
///
/// <para>Nivel BD. Siembra un fixture aislado por GUIDs frescos (artículos
/// <c>Guid.NewGuid()</c> → cero contaminación con datos de millet_dev). No monta
/// empresa: el adapter bypasea el query filter, así que EvaluarReorden funciona sin
/// contexto de empresa.</para>
/// </summary>
public class EvaluarReordenTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EvaluarReordenTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Faltante_N2_y_N1_interino_luego_con_vivo_de_sistema_y_dedup()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();

        var empresa = Guid.NewGuid();
        var suc = Guid.NewGuid();
        var almM = Guid.NewGuid(); var subM = Guid.NewGuid(); var ubM = Guid.NewGuid();
        var almM2 = Guid.NewGuid(); var subM2 = Guid.NewGuid(); var ubM2 = Guid.NewGuid();
        var artN2 = Guid.NewGuid();
        var artN1 = Guid.NewGuid();
        string K() => $"T{Guid.NewGuid():N}".Substring(0, 8);

        try
        {
            await SeedAlmacen(db, almM, suc, K());
            await SeedAlmacen(db, almM2, suc, K());
            await SeedSubUbic(db, subM, almM, ubM, K());
            await SeedSubUbic(db, subM2, almM2, ubM2, K());

            // Config N2 (artN2 en almM): objetivo Máximo=100. Existencia 30.
            await SeedConfig(db, artN2, nivel: 1, entidad: almM, min: 5, max: 100, reorden: 20, objetivo: 1);
            await SeedSaldo(db, ubM, subM, artN2, 30);

            // Config N1 (artN1 en sucursal): objetivo Máximo=200. Existencia 40 (M) + 10 (M2) = 50.
            await SeedConfig(db, artN1, nivel: 0, entidad: suc, min: 10, max: 200, reorden: 50, objetivo: 1);
            await SeedSaldo(db, ubM, subM, artN1, 40);
            await SeedSaldo(db, ubM2, subM2, artN1, 10);

            // ── Interino: sin RQ de sistema → vivo = 0 → faltante = objetivo − física ──
            var n2Interino = await Faltante(mediator, artN2);
            n2Interino.ExistenciaFisica.Should().Be(30m);
            n2Interino.Vivo.Should().Be(0m);
            n2Interino.Faltante.Should().Be(70m);   // 100 − 30 − 0

            var n1Interino = await Faltante(mediator, artN1);
            n1Interino.ExistenciaFisica.Should().Be(50m);  // rollup sucursal (40 + 10)
            n1Interino.Vivo.Should().Be(0m);
            n1Interino.Faltante.Should().Be(150m);   // 200 − 50 − 0

            // ── Seed de "lo vivo" ──
            // Manual live (Autorizada) de artN2 en almM: NO debe contar.
            var rqManual = Guid.NewGuid();
            await SeedRq(db, rqManual, empresa, origen: 0, estado: 2, almDest: almM, K());
            await SeedRqLinea(db, Guid.NewGuid(), rqManual, artN2, 50);

            // Sistema Borrador de artN2 en almM, sin OC: vivo += 25.
            var rqSisBorr = Guid.NewGuid();
            await SeedRq(db, rqSisBorr, empresa, origen: 1, estado: 0, almDest: almM, K());
            await SeedRqLinea(db, Guid.NewGuid(), rqSisBorr, artN2, 25);

            // Sistema EnSurtido de artN2 en almM (cant 40) + OC viva ligada (40, recibida 10):
            // dedup → OC pendiente 30, RQ 0. vivo += 30.
            var rqSisOc = Guid.NewGuid();
            var rqSisOcLinea = Guid.NewGuid();
            await SeedRq(db, rqSisOc, empresa, origen: 1, estado: 3, almDest: almM, K());
            await SeedRqLinea(db, rqSisOcLinea, rqSisOc, artN2, 40);
            var oc = Guid.NewGuid();
            await SeedOc(db, oc, empresa, estado: 3, K());
            await SeedOcLinea(db, Guid.NewGuid(), oc, artN2, cantidad: 40, recibida: 10,
                rqId: rqSisOc, rqLineaId: rqSisOcLinea);

            // Sistema Borrador de artN1 en almM2 (almacén de la sucursal): N1 vivo += 20.
            var rqSisN1 = Guid.NewGuid();
            await SeedRq(db, rqSisN1, empresa, origen: 1, estado: 0, almDest: almM2, K());
            await SeedRqLinea(db, Guid.NewGuid(), rqSisN1, artN1, 20);

            // ── Con vivo ──
            var n2 = await Faltante(mediator, artN2);
            n2.Vivo.Should().Be(55m);        // 25 (borrador) + 30 (OC pendiente); manual 50 IGNORADO
            n2.Faltante.Should().Be(15m);    // 100 − 30 − 55

            var n1 = await Faltante(mediator, artN1);
            n1.Vivo.Should().Be(20m);        // sumado a nivel sucursal (almM2)
            n1.Faltante.Should().Be(130m);   // 200 − 50 − 20
        }
        finally
        {
            await Limpiar(db, empresa, new[] { ubM, ubM2 }, new[] { subM, subM2 }, new[] { almM, almM2 },
                new[] { artN2, artN1 });
        }
    }

    [Fact]
    public async Task Adapter_de_vivo_devuelve_correcto_SIN_empresa_en_contexto()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var port = scope.ServiceProvider.GetRequiredService<IComprasPedidoVivoReadPort>();
        var empresaCtx = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();

        var empresa = Guid.NewGuid();
        var almB = Guid.NewGuid();
        var artB = Guid.NewGuid();
        string K() => $"T{Guid.NewGuid():N}".Substring(0, 8);

        try
        {
            // Sin empresa en contexto (como el worker de fondo). Si el bypass no
            // funcionara, el query filter dejaría el vivo en vacío.
            empresaCtx.Current.Should().BeNull();

            var rq = Guid.NewGuid();
            await SeedRq(db, rq, empresa, origen: 1, estado: 0, almDest: almB, K());
            await SeedRqLinea(db, Guid.NewGuid(), rq, artB, 10);

            var vivo = await port.ObtenerVivoDeSistemaAsync(
                new[] { new PedidoVivoClave(artB, almB) }, CancellationToken.None);

            vivo.Should().ContainKey(new PedidoVivoClave(artB, almB));
            vivo[new PedidoVivoClave(artB, almB)].Should().Be(10m);   // NO vacío → bypass OK
        }
        finally
        {
            await Limpiar(db, empresa, [], [], [], [artB]);
        }
    }

    private static async Task<FaltanteReorden> Faltante(IMediator mediator, Guid articuloId)
    {
        var lista = await mediator.Send(new EvaluarReordenQuery(ArticuloId: articuloId));
        lista.Should().ContainSingle();
        return lista[0];
    }

    // ── Seed helpers (raw SQL; columnas NOT NULL sin default) ──

    private static async Task SeedAlmacen(AlmacenDbContext db, Guid id, Guid suc, string clave) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
            VALUES ({id}, {clave}, 'RA', {suc}, 0, 0, NOW(), NOW())");

    private static async Task SeedSubUbic(AlmacenDbContext db, Guid subId, Guid alm, Guid ubic, string clave)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
            VALUES ({subId}, {alm}, {clave}, 'Sub', 0, 0, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({ubic}, {subId}, {clave}, 'Ubic', 0, false, 0, NOW(), NOW())");
    }

    private static async Task SeedSaldo(AlmacenDbContext db, Guid ubic, Guid sub, Guid art, decimal cantidad) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario
                (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
            VALUES ({ubic}, {sub}, {art}, {cantidad}, 0, NOW())");

    private static async Task SeedConfig(
        AlmacenDbContext db, Guid art, short nivel, Guid entidad,
        decimal min, decimal max, decimal reorden, short objetivo) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.configuraciones_reorden
                (id, articulo_id, nivel, entidad_id, minimo, maximo, punto_reorden, auto_requisicion, objetivo, estatus, version, created_at, updated_at)
            VALUES ({Guid.NewGuid()}, {art}, {nivel}, {entidad}, {min}, {max}, {reorden}, true, {objetivo}, 0, 0, NOW(), NOW())");

    private static async Task SeedRq(
        AlmacenDbContext db, Guid id, Guid empresa, short origen, short estado, Guid almDest, string folio) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.requisiciones
                (id, empresa_id, folio, folio_anio, clasificacion, sucursal_id, departamento_id, almacen_destino_id,
                 requisitante_id, creador_id, prioridad, fecha_solicitud, estado, origen, version, created_at, updated_at)
            VALUES ({id}, {empresa}, {folio}, 2027, 0, {Guid.NewGuid()}, {Guid.NewGuid()}, {almDest},
                    {Guid.NewGuid()}, {Guid.NewGuid()}, 0, NOW(), {estado}, {origen}, 0, NOW(), NOW())");

    private static async Task SeedRqLinea(AlmacenDbContext db, Guid id, Guid rqId, Guid art, decimal cantidad) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.requisicion_lineas
                (id, requisicion_id, posicion, articulo_id, cantidad, unidad_medida, precio_estimado, version, created_at, updated_at)
            VALUES ({id}, {rqId}, 1, {art}, {cantidad}, 'PZA', 0, 0, NOW(), NOW())");

    private static async Task SeedOc(
        AlmacenDbContext db, Guid id, Guid empresa, short estado, string folio) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.ordenes_compra
                (id, empresa_id, folio, folio_anio, proveedor_id, sucursal_destino_id,
                 condiciones_pago_id, uso_principal_id, comprador_titular_id, encargado_compras_id,
                 fecha_documento, estado, moneda, version, created_at, updated_at)
            VALUES ({id}, {empresa}, {folio}, 2027, {Guid.NewGuid()}, {Guid.NewGuid()},
                    {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                    CURRENT_DATE, {estado}, 'MXN', 0, NOW(), NOW())");

    // PR2: la línea de OC ya no guarda almacén. El almacén del reorden se lee de la
    // RQ de origen (requisicion_id) vía join en ComprasPedidoVivoReadAdapter.
    private static async Task SeedOcLinea(
        AlmacenDbContext db, Guid id, Guid ocId, Guid art,
        decimal cantidad, decimal recibida, Guid rqId, Guid rqLineaId) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.orden_compra_lineas
                (id, orden_compra_id, posicion, articulo_id, cantidad, unidad_medida, precio_unitario,
                 indicador_impuestos, departamento_solicitante_id, descuento_tipo, descuento_valor,
                 cantidad_recibida, requisicion_id, linea_requisicion_id, version, created_at, updated_at)
            VALUES ({id}, {ocId}, 1, {art}, {cantidad}, 'PZA', 0,
                    'IVA16', {Guid.NewGuid()}, 0, 0,
                    {recibida}, {rqId}, {rqLineaId}, 0, NOW(), NOW())");

    private static async Task Limpiar(
        AlmacenDbContext db, Guid empresa, Guid[] ubics, Guid[] subs, Guid[] alms, Guid[] articulos)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM compras.orden_compra_lineas WHERE orden_compra_id IN
                (SELECT id FROM compras.ordenes_compra WHERE empresa_id = {empresa})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.ordenes_compra WHERE empresa_id = {empresa}");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM compras.requisicion_lineas WHERE requisicion_id IN
                (SELECT id FROM compras.requisiciones WHERE empresa_id = {empresa})");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisiciones WHERE empresa_id = {empresa}");
        foreach (var art in articulos)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.configuraciones_reorden WHERE articulo_id = {art}");
        foreach (var ub in ubics)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ub}");
        foreach (var ub in ubics)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ub}");
        foreach (var sub in subs)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {sub}");
        foreach (var alm in alms)
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {alm}");
    }
}
