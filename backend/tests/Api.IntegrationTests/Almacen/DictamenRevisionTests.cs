using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Blob;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// ALM-04 G1.3-a/c con Postgres real y los triggers del ledger: liberar mueve saldo de MAT-REV al rack disponible
/// (par LIB + REI), la baja solo reduce MAT-REV, sin el permiso <c>almacen.cuarentena.dictaminar</c> la API responde 403,
/// el rechazo abre la devolución a proveedor existente con la evidencia (G1.3-b), y la reincorporación 8.A usa el mismo par (ya no crea existencia de la nada).
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class DictamenRevisionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    private readonly WebApplicationFactory<Program> _factory;

    private static readonly Guid TipoFotoDano = Guid.Parse("00000011-0006-0000-0000-000000000006");
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public DictamenRevisionTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            {
                s.AddSingleton<IBlobStoragePort>(new BlobGrabador());
                // El rechazo lee el proveedor de la OC por el puerto de Compras; la OC del fixture no existe en compras.*.
                s.AddScoped<IComprasOcReadPort, OcProveedorFake>();
            }));

    private sealed class OcProveedorFake : IComprasOcReadPort
    {
        private static readonly Guid ProveedorFijoId = Guid.NewGuid();
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) =>
            Task.FromResult<OcLectura?>(new(ocId, "OC-ALM04", ProveedorFijoId, Guid.NewGuid(), "Autorizada", []));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

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
    public async Task G13b_rechazar_abre_devolucion_en_autorizacion_con_la_evidencia_y_su_rechazo_regresa_el_pendiente()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        var lineaId = Guid.Empty;
        try
        {
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                lineaId = await RecibirConDanoAsync(db, ctx, 10m, 2m);
                var movimientoRevisionId = await db.LineasMovimiento.Where(l => l.Id == lineaId)
                    .Select(l => l.MovimientoId).SingleAsync();
                var cuerpo = new
                {
                    resultado = 1, cantidad = 1m, motivo = "No sirve", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                };

                // Sin evidencia: 422 y no se crea nada.
                using var sinEvidencia = await admin.PostAsJsonAsync(Ruta(lineaId), cuerpo);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, sinEvidencia.StatusCode);
                Assert.Equal("DICTAMEN_RECHAZO_SIN_EVIDENCIA", await ContabTestKit.Code(sinEvidencia));
                Assert.False(await db.LineasDevolucionProveedor.AnyAsync(l => l.LineaRecepcionOrigenId == lineaId));

                using var contenido = new MultipartFormDataContent();
                var archivo = new ByteArrayContent(Png);
                archivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                contenido.Add(archivo, "archivo", "dano.png");
                contenido.Add(new StringContent(TipoFotoDano.ToString()), "tipoDocumentoId");
                using var subida = await admin.PostAsync($"/api/v1/almacen/revision/lineas/{lineaId}/adjuntos", contenido);
                Assert.True(subida.IsSuccessStatusCode, await subida.Content.ReadAsStringAsync());

                using var resp = await admin.PostAsJsonAsync(Ruta(lineaId), cuerpo);
                Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
                var devolucionId = (await ContabTestKit.Json(resp)).GetProperty("devolucionAProveedorId").GetGuid();

                var dev = await ContabTestKit.Json(await admin.GetAsync($"/api/v1/almacen/devoluciones-proveedor/{devolucionId}"));
                Assert.Equal(movimientoRevisionId, dev.GetProperty("recepcionOrigenId").GetGuid());
                var evidencias = dev.GetProperty("evidencias").EnumerateArray().ToList();
                Assert.Single(evidencias);
                db.ChangeTracker.Clear();
                Assert.Equal(Millet.Almacen.Domain.DevolucionesProveedor.EstadoDevolucionProveedor.EnAutorizacion,
                    (await db.DevolucionesProveedor.SingleAsync(d => d.Id == devolucionId)).Estado);

                // De 2 en revisión, 1 queda en la devolución (pendiente 1) mientras vive; el saldo físico no se mueve hasta que Dirección autorice y registre.
                var lista = await ContabTestKit.Json(await admin.GetAsync("/api/v1/almacen/revision?soloPendientes=false"));
                Assert.Equal(1m, lista.EnumerateArray().Single(x => x.GetProperty("lineaRevisionId").GetGuid() == lineaId)
                    .GetProperty("cantidadPendiente").GetDecimal());
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));

                // Dirección rechaza la devolución: el pendiente regresa.
                using var rechazo = await admin.PostAsJsonAsync(
                    $"/api/v1/almacen/devoluciones-proveedor/{devolucionId}/rechazar", new { motivoRechazo = "Se queda en revisión" });
                Assert.Equal(HttpStatusCode.NoContent, rechazo.StatusCode);
                var despues = await ContabTestKit.Json(await admin.GetAsync("/api/v1/almacen/revision"));
                Assert.Equal(2m, despues.EnumerateArray().Single(x => x.GetProperty("lineaRevisionId").GetGuid() == lineaId)
                    .GetProperty("cantidadPendiente").GetDecimal());
            }, ContabTestKit.EmpresaBootstrapId);
        }
        finally
        {
            if (lineaId != Guid.Empty)
            {
                using var scope = _factory.Services.CreateScope();
                var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
                await almacen.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM compartido.adjuntos WHERE tipo_entidad = 'linea_revision' AND entidad_id = {lineaId}");
            }
        }
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

    private static ReincorporacionTrasRevisionHandler HandlerReincorporar(AlmacenDbContext db, Ctx ctx, string unidad = "PZA") =>
        new(db, new UserFake(ctx.UserId), new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(), ctx.Periodos, new ArticulosUnidad(unidad));

    /// <summary>Saldo 8.A ajeno a recepciones: un segundo bin de MAT-REV con existencia sin línea de recepción en revisión.</summary>
    internal static async Task<Guid> SembrarSaldo8AEnMatRevAsync(AlmacenDbContext db, Ctx ctx, decimal cantidad)
    {
        var bin = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.ubicaciones (id, sub_almacen_id, clave, nombre, estatus, es_default, version, created_at, updated_at)
            VALUES ({bin}, {ctx.SubRevisionId}, 'REV-02', 'Revisión 2 (8.A)', 0, false, 0, NOW(), NOW())");
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO almacen.saldos_inventario (ubicacion_id, sub_almacen_id, articulo_id, cantidad, costo_promedio_mxn, ultima_actualizacion_at)
            VALUES ({bin}, {ctx.SubRevisionId}, {ctx.ArticuloId}, {cantidad}, 10, NOW())");
        return bin;
    }

    [Fact]
    public async Task Reincorporacion_8A_descuenta_MAT_REV_ajeno_a_recepciones_y_suma_al_destino_sin_tocar_lo_reservado()
    {
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirConDanoAsync(db, ctx, 10m, 2m); // R-1 = 8, REV-01 = 2 (reservado para dictamen)
            var bin8A = await SembrarSaldo8AEnMatRevAsync(db, ctx, 3m);

            await HandlerReincorporar(db, ctx).Handle(
                new ReincorporacionTrasRevisionCommand(ctx.SubDisponibleId, Fecha, "Revisión ok",
                    [new MatRevLineaInput(ctx.ArticuloId, 1m, ctx.RackId)]),
                CancellationToken.None);

            Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId)); // reservado intacto
            Assert.Equal(2m, await LeerSaldoAsync(db, bin8A, ctx.ArticuloId));
            Assert.Equal(9m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
            Assert.Equal(1, await db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.SalidaPorLiberacionRevision
                && m.Lineas.Any(l => l.UbicacionId == bin8A)));
        });
    }

    [Fact]
    public async Task Reincorporacion_8A_usa_la_unidad_base_del_articulo_no_PZA()
    {
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await SembrarSaldo8AEnMatRevAsync(db, ctx, 3m);

            await HandlerReincorporar(db, ctx, "M2").Handle(
                new ReincorporacionTrasRevisionCommand(ctx.SubDisponibleId, Fecha, "Revisión ok",
                    [new MatRevLineaInput(ctx.ArticuloId, 1.5m, ctx.RackId)]),
                CancellationToken.None);

            db.ChangeTracker.Clear();
            var unidades = await db.Movimientos
                .Where(m => m.Tipo == TipoMovimiento.SalidaPorLiberacionRevision || m.Tipo == TipoMovimiento.ReincorporacionTrasRevision)
                .SelectMany(m => m.Lineas.Where(l => l.ArticuloId == ctx.ArticuloId).Select(l => l.UnidadMedida)).ToListAsync();
            Assert.Equal(2, unidades.Count);
            Assert.All(unidades, u => Assert.Equal("M2", u));
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

    [Fact]
    public async Task Reincorporacion_8A_no_puede_consumir_lo_reservado_para_dictamen()
    {
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirConDanoAsync(db, ctx, 10m, 2m); // REV-01 = 2 sin dictaminar

            var ex = await Assert.ThrowsAsync<Millet.SharedKernel.Application.Exceptions.BusinessRuleException>(() =>
                HandlerReincorporar(db, ctx).Handle(
                    new ReincorporacionTrasRevisionCommand(ctx.SubDisponibleId, Fecha, "Revisión",
                        [new MatRevLineaInput(ctx.ArticuloId, 1m, ctx.RackId)]),
                    CancellationToken.None));
            Assert.Equal("MATREV_RESERVADO_PARA_DICTAMEN", ex.Code);
            Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
            Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
        });
    }

    [Fact]
    public async Task G13b_ciclo_completo_rechazo_autoriza_direccion_registra_salida_deja_MAT_REV_en_cero_y_publica_evento()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        var lineaId = Guid.Empty;
        var devolucionId = Guid.Empty;
        try
        {
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                lineaId = await RecibirConDanoAsync(db, ctx, 10m, 2m);
                using var contenido = new MultipartFormDataContent();
                var archivo = new ByteArrayContent(Png);
                archivo.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                contenido.Add(archivo, "archivo", "dano.png");
                contenido.Add(new StringContent(TipoFotoDano.ToString()), "tipoDocumentoId");
                using var subida = await admin.PostAsync($"/api/v1/almacen/revision/lineas/{lineaId}/adjuntos", contenido);
                Assert.True(subida.IsSuccessStatusCode, await subida.Content.ReadAsStringAsync());

                // Rechazo de las 2 unidades en revisión: abre la devolución a proveedor en autorización.
                using var rechazo = await admin.PostAsJsonAsync(Ruta(lineaId), new
                {
                    resultado = 1, cantidad = 2m, motivo = "No sirve", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                });
                Assert.True(rechazo.StatusCode == HttpStatusCode.Created, await rechazo.Content.ReadAsStringAsync());
                devolucionId = (await ContabTestKit.Json(rechazo)).GetProperty("devolucionAProveedorId").GetGuid();
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId)); // el físico no se mueve aún

                // Dirección autoriza y el almacenista registra la salida física desde el bin de MAT-REV.
                using var autoriza = await admin.PostAsync($"/api/v1/almacen/devoluciones-proveedor/{devolucionId}/autorizar", null);
                Assert.True(autoriza.StatusCode == HttpStatusCode.NoContent, await autoriza.Content.ReadAsStringAsync());
                var dev = await ContabTestKit.Json(await admin.GetAsync($"/api/v1/almacen/devoluciones-proveedor/{devolucionId}"));
                var lineaDevolucionId = dev.GetProperty("lineas").EnumerateArray().Single().GetProperty("id").GetGuid();
                using var salida = await admin.PostAsJsonAsync($"/api/v1/almacen/devoluciones-proveedor/{devolucionId}/registrar-salida", new
                {
                    subAlmacenId = ctx.SubRevisionId,
                    fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                    bins = new[] { new { lineaDevolucionId, ubicacionId = ctx.RackRevisionId } },
                });
                Assert.True(salida.StatusCode == HttpStatusCode.OK, await salida.Content.ReadAsStringAsync());

                Assert.Equal(0m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
                Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
                var entradas = await db.OutboxEntries.AsNoTracking()
                    .Where(e => e.EventType == "almacen.oc_devolucion.registrada.v1").ToListAsync();
                var entrada = Assert.Single(entradas, e => e.Payload.Contains(devolucionId.ToString(), StringComparison.OrdinalIgnoreCase));
                using var payload = System.Text.Json.JsonDocument.Parse(entrada.Payload);
                var movimientoRevisionId = await db.LineasMovimiento.Where(l => l.Id == lineaId).Select(l => l.MovimientoId).SingleAsync();
                Assert.Equal(movimientoRevisionId, payload.RootElement.GetProperty("RecepcionOrigenId").GetGuid());
                Assert.Equal(2m, payload.RootElement.GetProperty("Lineas").EnumerateArray().Single().GetProperty("Cantidad").GetDecimal());
            }, ContabTestKit.EmpresaBootstrapId);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var almacen = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
            if (lineaId != Guid.Empty)
                await almacen.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM compartido.adjuntos WHERE tipo_entidad = 'linea_revision' AND entidad_id = {lineaId}");
            if (devolucionId != Guid.Empty)
            {
                var ids = (await almacen.OutboxEntries.AsNoTracking()
                    .Where(e => e.EventType == "almacen.oc_devolucion.registrada.v1").ToListAsync())
                    .Where(e => e.Payload.Contains(devolucionId.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Id).ToArray();
                await almacen.OutboxEntries.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync();
            }
        }
    }
}
