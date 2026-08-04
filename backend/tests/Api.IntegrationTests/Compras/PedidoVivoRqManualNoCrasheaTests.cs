using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Guardrail del BLOCKER resuelto en almacén-por-línea PR3.
///
/// <para>Post-PR3 la RQ manual tiene <c>almacen_destino_id = NULL</c>. El
/// pedido-vivo (<see cref="IComprasPedidoVivoReadPort"/>) hace join OC→RQ y
/// proyecta <c>rq.AlmacenDestinoId</c> en un <c>Guid</c> no-nullable. Sin el
/// filtro <c>Origen == Sistema</c> en SQL, una OC autorizada creada desde una
/// RQ MANUAL (almacén NULL) haría materializar NULL → excepción. El fix (Card A)
/// agrega ese filtro al WHERE del OC-side, excluyendo las líneas de origen
/// manual antes de proyectar.</para>
///
/// <para>Este test siembra AMBOS casos con el mismo artículo y verifica: (1) no
/// se lanza excepción; (2) la OC de origen manual NO se cuenta; (3) la OC de
/// origen sistema SÍ se cuenta (regresión del reorden). Datos sembrados por el
/// test (raw SQL), no dependen del seed de millet_dev.</para>
/// </summary>
public class PedidoVivoRqManualNoCrasheaTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PedidoVivoRqManualNoCrasheaTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OC_autorizada_desde_RQ_manual_no_crashea_y_queda_excluida()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var puerto = scope.ServiceProvider.GetRequiredService<IComprasPedidoVivoReadPort>();

        var empresa = Guid.NewGuid();
        var articulo = Guid.NewGuid();
        var almSistema = Guid.NewGuid();

        // Sistema: RQ origen=1 con almacén poblado, línea (art, 40), OC autorizada
        // ligada (40, recibida 10) → OC pendiente 30 para (art, almSistema).
        var rqSis = Guid.NewGuid(); var rqSisLinea = Guid.NewGuid();
        var ocSis = Guid.NewGuid();
        // Manual: RQ origen=0 con almacén NULL, línea (art, 100), OC autorizada
        // ligada (100, recibida 0) → sería (art, NULL) y CRASHEARÍA sin el filtro.
        var rqMan = Guid.NewGuid(); var rqManLinea = Guid.NewGuid();
        var ocMan = Guid.NewGuid();

        try
        {
            await SeedRq(db, rqSis, empresa, origen: 1, almacen: almSistema, "PV-SIS");
            await SeedRqLinea(db, rqSisLinea, rqSis, articulo, 40);
            await SeedOc(db, ocSis, empresa, "PV-OC-SIS");
            await SeedOcLinea(db, Guid.NewGuid(), ocSis, articulo, 40, recibida: 10, rqSis, rqSisLinea);

            await SeedRq(db, rqMan, empresa, origen: 0, almacen: null, "PV-MAN");
            await SeedRqLinea(db, rqManLinea, rqMan, articulo, 100);
            await SeedOc(db, ocMan, empresa, "PV-OC-MAN");
            await SeedOcLinea(db, Guid.NewGuid(), ocMan, articulo, 100, recibida: 0, rqMan, rqManLinea);

            // (1) No crashea (sin el filtro Origen==Sistema, materializar la OC
            // manual con almacén NULL en el Guid de OcRow lanzaría aquí).
            var vivo = await puerto.ObtenerVivoDeSistemaAsync(
                new[] { new PedidoVivoClave(articulo, almSistema) }, CancellationToken.None);

            // (2)+(3): solo cuenta la OC de origen sistema (30); la manual excluida.
            vivo.Should().ContainKey(new PedidoVivoClave(articulo, almSistema));
            vivo[new PedidoVivoClave(articulo, almSistema)].Should().Be(30m);
            vivo.Should().HaveCount(1);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compras.orden_compra_lineas WHERE orden_compra_id IN ({ocSis}, {ocMan})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compras.ordenes_compra WHERE id IN ({ocSis}, {ocMan})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compras.requisicion_lineas WHERE requisicion_id IN ({rqSis}, {rqMan})");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM compras.requisiciones WHERE id IN ({rqSis}, {rqMan})");
        }
    }

    private static async Task SeedRq(
        ComprasDbContext db, Guid id, Guid empresa, short origen, Guid? almacen, string folio) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.requisiciones
                (id, empresa_id, folio, folio_anio, clasificacion, sucursal_id, departamento_id, almacen_destino_id,
                 requisitante_id, creador_id, prioridad, fecha_solicitud, estado, origen, version, created_at, updated_at)
            VALUES ({id}, {empresa}, {folio}, 2027, 0, {Guid.NewGuid()}, {Guid.NewGuid()}, {almacen},
                    {Guid.NewGuid()}, {Guid.NewGuid()}, 0, NOW(), 3, {origen}, 0, NOW(), NOW())");

    private static async Task SeedRqLinea(ComprasDbContext db, Guid id, Guid rqId, Guid art, decimal cantidad) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.requisicion_lineas
                (id, requisicion_id, posicion, articulo_id, cantidad, unidad_medida, precio_estimado, version, created_at, updated_at)
            VALUES ({id}, {rqId}, 1, {art}, {cantidad}, 'PZA', 0, 0, NOW(), NOW())");

    // OC autorizada (estado=3). Post-PR2 la OC ya no tiene columnas de almacén.
    private static async Task SeedOc(ComprasDbContext db, Guid id, Guid empresa, string folio) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.ordenes_compra
                (id, empresa_id, folio, folio_anio, proveedor_id, sucursal_destino_id,
                 condiciones_pago_id, uso_principal_id, comprador_titular_id, encargado_compras_id,
                 fecha_documento, estado, moneda, version, created_at, updated_at)
            VALUES ({id}, {empresa}, {folio}, 2027, {Guid.NewGuid()}, {Guid.NewGuid()},
                    {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()}, {Guid.NewGuid()},
                    CURRENT_DATE, 3, 'MXN', 0, NOW(), NOW())");

    private static async Task SeedOcLinea(
        ComprasDbContext db, Guid id, Guid ocId, Guid art, decimal cantidad, decimal recibida, Guid rqId, Guid rqLineaId) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO compras.orden_compra_lineas
                (id, orden_compra_id, posicion, articulo_id, cantidad, unidad_medida, precio_unitario,
                 indicador_impuestos, departamento_solicitante_id, descuento_tipo, descuento_valor,
                 cantidad_recibida, requisicion_id, linea_requisicion_id, version, created_at, updated_at)
            VALUES ({id}, {ocId}, 1, {art}, {cantidad}, 'PZA', 0,
                    'IVA16', {Guid.NewGuid()}, 0, 0,
                    {recibida}, {rqId}, {rqLineaId}, 0, NOW(), NOW())");
}
