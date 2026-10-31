using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Reorden;
using Millet.Almacen.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-ALM-10 CA3: una requisición viva de la aplicación de Almacenes (origen 2, sin almacén destino,
/// por sucursal) cuenta como pedido vivo; el motor no pide lo que ya está pedido. Escrita para CI.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class ReordenRequisicionAppAlmacenesTests
{
    private readonly WebApplicationFactory<Program> _factory;
    public ReordenRequisicionAppAlmacenesTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Requisicion_viva_de_la_app_de_Almacenes_cuenta_como_vivo_y_el_motor_no_repone_de_mas()
    {
        using var scope = _factory.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        var empresa = Guid.NewGuid(); var suc = Guid.NewGuid(); var alm = Guid.NewGuid();
        // Usar artículo estándar sembrado (ART-EST-01) con unidad base PZA para que la conversión de unidad resuelva en catálogo.
        var art = Guid.Parse("00000005-0002-0000-0000-000000000001");
        var sub = Guid.NewGuid(); var ubic = Guid.NewGuid(); var rq = Guid.NewGuid();
        var clave = $"T{Guid.NewGuid():N}"[..8];
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.almacenes (id, clave, nombre, sucursal_id, estatus, version, created_at, updated_at)
                VALUES ({alm}, {clave}, 'RA', {suc}, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.sub_almacenes (id, almacen_id, clave, nombre, tipo, estatus, version, created_at, updated_at)
                VALUES ({sub}, {alm}, {clave}, 'Sub', 0, 0, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
                VALUES ({ubic}, {sub}, {clave}, 'Ubic', 0, false, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.saldos_inventario (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
                VALUES ({ubic}, {sub}, {art}, 8, 0, NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO almacen.configuraciones_reorden
                    (id, articulo_id, nivel, entidad_id, minimo, maximo, punto_reorden, auto_requisicion, objetivo, estatus, version, created_at, updated_at)
                VALUES ({Guid.NewGuid()}, {art}, 0, {suc}, 5, 40, 10, true, 1, 0, 0, NOW(), NOW())");

            var antes = Assert.Single(await mediator.Send(new EvaluarReordenQuery(ArticuloId: art)));
            antes.Vivo.Should().Be(0m);
            antes.Faltante.Should().Be(32m);   // existencia 8, vivo 0, punto 10, máximo 40

            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO compras.requisiciones
                    (id, empresa_id, folio, folio_anio, clasificacion, sucursal_id, departamento_id, almacen_destino_id,
                     requisitante_id, creador_id, prioridad, fecha_solicitud, estado, origen, version, created_at, updated_at)
                VALUES ({rq}, {empresa}, {clave}, 2027, 0, {suc}, {Guid.NewGuid()}, NULL,
                        {Guid.NewGuid()}, {Guid.NewGuid()}, 0, NOW(), 0, 2, 0, NOW(), NOW())");
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO compras.requisicion_lineas
                    (id, requisicion_id, posicion, articulo_id, cantidad, unidad_medida, precio_estimado, version, created_at, updated_at)
                VALUES ({Guid.NewGuid()}, {rq}, 1, {art}, 32, 'PZA', 0, 0, NOW(), NOW())");

            var despues = Assert.Single(await mediator.Send(new EvaluarReordenQuery(ArticuloId: art)));
            despues.Vivo.Should().Be(32m);
            despues.Faltante.Should().Be(0m);   // posición 40 > punto 10: no crea otra
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisicion_lineas WHERE requisicion_id = {rq}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM compras.requisiciones WHERE id = {rq}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.configuraciones_reorden WHERE articulo_id = {art}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.saldos_inventario WHERE ubicacion_id = {ubic}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.ubicaciones WHERE id = {ubic}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.sub_almacenes WHERE id = {sub}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.almacenes WHERE id = {alm}");
        }
    }
}
