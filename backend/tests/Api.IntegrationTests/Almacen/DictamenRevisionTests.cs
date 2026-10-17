using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// ALM-04 G1.3-a/c con Postgres real y los triggers del ledger: liberar mueve saldo de MAT-REV al rack disponible
/// (par LIB + REI), la baja solo reduce MAT-REV, sin el permiso <c>almacen.cuarentena.dictaminar</c> la API responde 403,
/// y la reincorporación 8.A usa el mismo par (ya no crea existencia de la nada).
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class DictamenRevisionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    private readonly WebApplicationFactory<Program> _factory;

    public DictamenRevisionTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static async Task<Guid> RecibirConDanoAsync(AlmacenDbContext db, Ctx ctx, decimal total, decimal danadas)
    {
        await HandlerRecepcion(db, new CapturaEventos(), ctx).Handle(
            new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null,
                [new RegistrarRecepcionLineaInput(ctx.ArticuloId, ctx.LineaOcId, total, null, null, ctx.RackId,
                    CantidadDanada: danadas, MotivoDano: "Hojas rotas")]),
            CancellationToken.None);
        db.ChangeTracker.Clear();
        return await db.LineasMovimiento.Where(l => l.UbicacionId == ctx.RackRevisionId).Select(l => l.Id)
            .OrderByDescending(id => id).FirstAsync();
    }

    private static string Ruta(Guid lineaId) => $"/api/v1/almacen/revision/lineas/{lineaId}/dictamen";

    [Fact]
    public async Task G13a_liberar_mueve_saldo_a_R1_y_la_bandeja_muestra_pendiente_e_historial()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var lineaId = await RecibirConDanoAsync(db, ctx, 10m, 2m);

            using var resp = await admin.PostAsJsonAsync(Ruta(lineaId), new
            {
                resultado = 0, cantidad = 1m, motivo = "Hoja utilizable", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                ubicacionDestinoId = ctx.RackId,
            });
            Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());

            Assert.Equal(9m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
            Assert.Equal(1m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));

            var lista = await ContabTestKit.Json(await admin.GetAsync("/api/v1/almacen/revision"));
            var fila = lista.EnumerateArray().Single(x => x.GetProperty("lineaRevisionId").GetGuid() == lineaId);
            Assert.Equal(1m, fila.GetProperty("cantidadPendiente").GetDecimal());
            Assert.Single(fila.GetProperty("dictamenes").EnumerateArray());

            // Excede lo pendiente: 422 y saldos sin cambio.
            using var excede = await admin.PostAsJsonAsync(Ruta(lineaId), new
            {
                resultado = 0, cantidad = 2m, motivo = "x", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"), ubicacionDestinoId = ctx.RackId,
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, excede.StatusCode);
            Assert.Equal("DICTAMEN_EXCEDE_PENDIENTE", await ContabTestKit.Code(excede));
            Assert.Equal(1m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
        }, ContabTestKit.EmpresaBootstrapId);
    }

    [Fact]
    public async Task G13c_sin_permiso_dictaminar_responde_403_y_el_saldo_no_cambia()
    {
        var operativo = await ContabTestKit.ClienteConPermisosAsync(_factory,
            PermisosCanonicos.AlmacenEntradasLeer, PermisosCanonicos.AlmacenEntradasCapturar);
        try
        {
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                var lineaId = await RecibirConDanoAsync(db, ctx, 10m, 2m);
                using var resp = await operativo.PostAsJsonAsync(Ruta(lineaId), new
                {
                    resultado = 0, cantidad = 1m, motivo = "Intento", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                    ubicacionDestinoId = ctx.RackId,
                });
                Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
                Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
            }, ContabTestKit.EmpresaBootstrapId);
        }
        finally { await ContabTestKit.LimpiarClienteAsync(operativo); }
    }

    [Fact]
    public async Task Baja_por_dano_reduce_solo_MAT_REV()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var lineaId = await RecibirConDanoAsync(db, ctx, 10m, 2m);
            using var resp = await admin.PostAsJsonAsync(Ruta(lineaId), new
            {
                resultado = 2, cantidad = 1m, motivo = "Rota", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
            });
            Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
            Assert.Equal(1m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
            Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
        }, ContabTestKit.EmpresaBootstrapId);
    }

    private static ReincorporacionTrasRevisionHandler HandlerReincorporar(AlmacenDbContext db, Ctx ctx) =>
        new(db, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(), ctx.Periodos);

    [Fact]
    public async Task Reincorporacion_8A_descuenta_MAT_REV_y_suma_al_destino_en_la_misma_cantidad()
    {
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirConDanoAsync(db, ctx, 10m, 2m); // R-1 = 8, REV-01 = 2

            await HandlerReincorporar(db, ctx).Handle(
                new ReincorporacionTrasRevisionCommand(ctx.SubDisponibleId, Fecha, "Revisión ok",
                    [new MatRevLineaInput(ctx.ArticuloId, 1m, ctx.RackId)]),
                CancellationToken.None);

            Assert.Equal(1m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
            Assert.Equal(9m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
            Assert.Equal(1, await db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.SalidaPorLiberacionRevision
                && m.Lineas.Any(l => l.UbicacionId == ctx.RackRevisionId)));
        });
    }

    [Fact]
    public async Task Reincorporacion_8A_sin_saldo_suficiente_en_MAT_REV_falla_y_no_crea_existencia()
    {
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirConDanoAsync(db, ctx, 10m, 2m); // REV-01 = 2

            var act = () => HandlerReincorporar(db, ctx).Handle(
                new ReincorporacionTrasRevisionCommand(ctx.SubDisponibleId, Fecha, "Revisión",
                    [new MatRevLineaInput(ctx.ArticuloId, 5m, ctx.RackId)]),
                CancellationToken.None);
            var ex = await Assert.ThrowsAnyAsync<Exception>(act);
            Assert.Contains("SALDO_INSUFICIENTE", ex.ToString());

            Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
            Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
        });
    }
}
