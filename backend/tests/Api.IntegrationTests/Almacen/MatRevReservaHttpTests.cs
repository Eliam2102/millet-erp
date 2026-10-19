using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Application.UnidadesMedida;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// ALM-04 por HTTP con Postgres real: (1) un almacenista de 8.A (solo <c>devoluciones-internas.capturar</c>) no puede
/// consumir con baja-por-daño / reincorporar lo que está en MAT-REV por una recepción sin dictaminar (422
/// <c>MATREV_RESERVADO_PARA_DICTAMEN</c>) pero sí el saldo 8.A ajeno a recepciones; (9a) el endpoint real de recepción
/// acepta <c>cantidadDanada</c>; (9b) un usuario de otra sucursal ve 0 filas en la bandeja y recibe 403 al dictaminar.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class MatRevReservaHttpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    private static readonly Guid SucursalMid = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly Guid SucursalOtra = Guid.Parse("00000005-0003-0000-0000-000000000002");
    private static Ctx? _ctxActual; // la colección corre en serie: los puertos falsos leen el fixture en curso

    private readonly WebApplicationFactory<Program> _factory;

    public MatRevReservaHttpTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            // El fixture no existe en datos maestros ni en compras: se sustituyen los puertos de lectura.
            s.AddSingleton<IBlobStoragePort>(new BlobGrabador()); // adjunto de evidencia del rechazo
            s.AddScoped<IArticuloReadPort, ArticulosUnidad>(_ => new ArticulosUnidad());
            s.AddScoped<IComprasOcReadPort, OcDelFixture>();
            s.AddScoped<IConversionUnidadPort, ConversionIdentidad>();
            // La OC del fixture no existe en compras: su sucursal es la del almacén del fixture (Mid).
            s.AddScoped<IComprasSucursalReadPort, OcSucursalDelFixture>();
        }));

    private sealed class OcDelFixture : IComprasOcReadPort
    {
        private static readonly Guid ProveedorFijoId = Guid.NewGuid(); // el rechazo exige el proveedor de la OC
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct)
        {
            var c = _ctxActual!;
            return Task.FromResult<OcLectura?>(new(ocId, "OC-ALM04", ProveedorFijoId, c.EmpresaId, "Autorizada",
                [new OcLineaLectura(c.LineaOcId, c.ArticuloId, "PZA", 10m, 0m, 100m)]));
        }
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class OcSucursalDelFixture : IComprasSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DocumentoSucursales>>(
                tipo == "orden_compra" && _ctxActual is { } c ? [new(c.OcId, [SucursalMid])] : []);
    }

    private sealed class ConversionIdentidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? capturada, string documento, CancellationToken ct)
        {
            var unidad = string.IsNullOrWhiteSpace(documento) ? "PZA" : documento;
            return Task.FromResult(new ConversionUnidad(cantidad, unidad, cantidad, 1, unidad, cantidad));
        }
    }

    // ─── Usuarios con rol de permisos acotados y sucursal asociada (ADR-0051), limpiados al terminar ───
    private sealed record Operativo(HttpClient Client, Guid UsuarioId, Guid RolId);

    private async Task<Operativo> CrearOperativoAsync(Guid sucursalId, params string[] permisos)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        var sufijo = Guid.NewGuid().ToString("N")[..10];
        var oid = $"alm04-{sufijo}";
        var rolId = Guid.CreateVersion7();
        var usuarioId = Guid.CreateVersion7();
        identidad.Roles.Add(new Rol(rolId, $"rol-alm04-{sufijo}", "Rol de prueba ALM-04"));
        foreach (var codigo in permisos)
        {
            var permisoId = await identidad.Permisos.AsNoTracking().Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
            identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
        }
        identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Operativo ALM-04"));
        identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
        identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId,
            ContabTestKit.EmpresaBootstrapId, rolId, asignadoPorUsuarioId: null));
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), usuarioId, sucursalId, ContabTestKit.EmpresaBootstrapId));
        await identidad.SaveChangesAsync();
        return new Operativo(await ContabTestKit.LoginAsync(_factory, oid, $"{oid}@test.local"), usuarioId, rolId);
    }

    private async Task LimpiarAsync(Operativo? o)
    {
        if (o is null) return;
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        await db.UsuarioSucursales.Where(a => a.UsuarioId == o.UsuarioId).ExecuteDeleteAsync();
        await db.UsuarioEmpresaRoles.Where(a => a.UsuarioId == o.UsuarioId).ExecuteDeleteAsync();
        await db.UsuarioPreferencias.Where(a => a.UsuarioId == o.UsuarioId).ExecuteDeleteAsync();
        await db.Usuarios.Where(u => u.Id == o.UsuarioId).ExecuteDeleteAsync();
        await db.RolPermisos.Where(p => p.RolId == o.RolId).ExecuteDeleteAsync();
        await db.Roles.Where(r => r.Id == o.RolId).ExecuteDeleteAsync();
        o.Client.Dispose();
    }

    private static async Task PonerAlmacenEnSucursalAsync(AlmacenDbContext db, Guid almacenId, Guid sucursalId) =>
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE almacen.almacenes SET sucursal_id = {sucursalId} WHERE id = {almacenId}");

    private static async Task<Guid> RecibirConDanoAsync(AlmacenDbContext db, Ctx ctx)
    {
        await HandlerRecepcion(db, new CapturaEventos(), ctx).Handle(
            new Millet.Almacen.Application.Recepciones.RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null,
                "AD662D33-6934-459C-A128-BDF0393E0062", null,
                [new Millet.Almacen.Application.Recepciones.RegistrarRecepcionLineaInput(ctx.ArticuloId, ctx.LineaOcId, 10m,
                    null, null, ctx.RackId, CantidadDanada: 2m, MotivoDano: "Hojas rotas")]),
            CancellationToken.None);
        db.ChangeTracker.Clear();
        return await db.LineasMovimiento.Where(l => l.UbicacionId == ctx.RackRevisionId).Select(l => l.Id).SingleAsync();
    }

    private static object Baja(Ctx ctx, Guid bin, decimal cantidad) => new
    {
        subAlmacenMatRevId = ctx.SubRevisionId, fechaMovimiento = Fecha.ToString("yyyy-MM-dd"), motivo = "Daño",
        lineas = new[] { new { articuloId = ctx.ArticuloId, cantidad, ubicacionId = bin } },
    };

    private static object Reincorporar(Ctx ctx, decimal cantidad) => new
    {
        subAlmacenDestinoId = ctx.SubDisponibleId, fechaMovimiento = Fecha.ToString("yyyy-MM-dd"), motivo = "Revisión",
        lineas = new[] { new { articuloId = ctx.ArticuloId, cantidad, ubicacionId = ctx.RackId } },
    };

    [Fact]
    public async Task Almacenista_8A_sin_dictaminar_no_consume_lo_de_una_recepcion_pero_si_el_saldo_8A_ajeno()
    {
        Operativo? operativo = null;
        try
        {
            operativo = await CrearOperativoAsync(SucursalMid, PermisosCanonicos.AlmacenDevolucionesInternasCapturar);
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                _ctxActual = ctx;
                await PonerAlmacenEnSucursalAsync(db, ctx.AlmacenId, SucursalMid);
                await RecibirConDanoAsync(db, ctx); // REV-01 = 2, reservado para dictamen

                // (1) Baja y reincorporación sobre lo de la recepción: 422 y el saldo no cambia.
                using var baja = await operativo.Client.PostAsJsonAsync("/api/v1/almacen/mat-rev/baja-por-dano", Baja(ctx, ctx.RackRevisionId, 1m));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, baja.StatusCode);
                Assert.Equal("MATREV_RESERVADO_PARA_DICTAMEN", await ContabTestKit.Code(baja));
                using var reinc = await operativo.Client.PostAsJsonAsync("/api/v1/almacen/mat-rev/reincorporar", Reincorporar(ctx, 1m));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, reinc.StatusCode);
                Assert.Equal("MATREV_RESERVADO_PARA_DICTAMEN", await ContabTestKit.Code(reinc));
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
                Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));

                // (2) El saldo 8.A ajeno a recepciones sí se consume (reincorporar 1 y dar de baja 2).
                var bin8A = await DictamenRevisionTests.SembrarSaldo8AEnMatRevAsync(db, ctx, 3m);
                using var reincOk = await operativo.Client.PostAsJsonAsync("/api/v1/almacen/mat-rev/reincorporar", Reincorporar(ctx, 1m));
                Assert.True(reincOk.StatusCode == HttpStatusCode.Created, await reincOk.Content.ReadAsStringAsync());
                using var bajaOk = await operativo.Client.PostAsJsonAsync("/api/v1/almacen/mat-rev/baja-por-dano", Baja(ctx, bin8A, 2m));
                Assert.True(bajaOk.StatusCode == HttpStatusCode.Created, await bajaOk.Content.ReadAsStringAsync());
                Assert.Equal(0m, await LeerSaldoAsync(db, bin8A, ctx.ArticuloId));
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId)); // lo reservado, intacto
                Assert.Equal(9m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
            }, ContabTestKit.EmpresaBootstrapId);
        }
        finally { await LimpiarAsync(operativo); }
    }

    private static readonly Guid TipoFotoDano = Guid.Parse("00000011-0006-0000-0000-000000000006");
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    [Fact]
    public async Task Baja_8A_no_consume_hoja_comprometida_en_devolucion_a_proveedor_abierta_hasta_su_salida_fisica()
    {
        Operativo? operativo = null;
        var admin = await ContabTestKit.LoginAsync(_factory);
        var lineaId = Guid.Empty;
        var devolucionId = Guid.Empty;
        try
        {
            operativo = await CrearOperativoAsync(SucursalMid, PermisosCanonicos.AlmacenDevolucionesInternasCapturar);
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                _ctxActual = ctx;
                await PonerAlmacenEnSucursalAsync(db, ctx.AlmacenId, SucursalMid);
                lineaId = await RecibirConDanoAsync(db, ctx); // REV-01 = 2
                using var contenido = new MultipartFormDataContent();
                var archivo = new ByteArrayContent(Png);
                archivo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                contenido.Add(archivo, "archivo", "dano.png");
                contenido.Add(new StringContent(TipoFotoDano.ToString()), "tipoDocumentoId");
                using var subida = await admin.PostAsync($"/api/v1/almacen/revision/lineas/{lineaId}/adjuntos", contenido);
                Assert.True(subida.IsSuccessStatusCode, await subida.Content.ReadAsStringAsync());

                // Rechazo al proveedor: la devolución queda EnAutorizacion y el físico sigue en MAT-REV.
                using var rechazo = await admin.PostAsJsonAsync($"/api/v1/almacen/revision/lineas/{lineaId}/dictamen", new
                {
                    resultado = 1, cantidad = 2m, motivo = "No sirve", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                });
                Assert.True(rechazo.StatusCode == HttpStatusCode.Created, await rechazo.Content.ReadAsStringAsync());
                devolucionId = (await ContabTestKit.Json(rechazo)).GetProperty("devolucionAProveedorId").GetGuid();

                using var baja = await operativo.Client.PostAsJsonAsync("/api/v1/almacen/mat-rev/baja-por-dano", Baja(ctx, ctx.RackRevisionId, 1m));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, baja.StatusCode);
                Assert.Equal("MATREV_RESERVADO_PARA_DICTAMEN", await ContabTestKit.Code(baja));
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));

                // Autorizada y con salida física registrada: otra hoja libre de MAT-REV sí se da de baja.
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

                var bin8A = await DictamenRevisionTests.SembrarSaldo8AEnMatRevAsync(db, ctx, 3m);
                using var bajaOk = await operativo.Client.PostAsJsonAsync("/api/v1/almacen/mat-rev/baja-por-dano", Baja(ctx, bin8A, 2m));
                Assert.True(bajaOk.StatusCode == HttpStatusCode.Created, await bajaOk.Content.ReadAsStringAsync());
                Assert.Equal(1m, await LeerSaldoAsync(db, bin8A, ctx.ArticuloId));
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
            await LimpiarAsync(operativo);
        }
    }

    [Fact]
    public async Task Listado_de_saldos_muestra_disponible_cero_en_MAT_REV()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            _ctxActual = ctx;
            await RecibirConDanoAsync(db, ctx);

            var rev = await ContabTestKit.Json(await admin.GetAsync($"/api/v1/almacen/saldos?subAlmacenId={ctx.SubRevisionId}"));
            var filaRev = rev.GetProperty("items").EnumerateArray().Single();
            Assert.Equal(2m, filaRev.GetProperty("cantidad").GetDecimal());
            Assert.Equal(0m, filaRev.GetProperty("cantidadDisponible").GetDecimal());

            var disp = await ContabTestKit.Json(await admin.GetAsync($"/api/v1/almacen/saldos?subAlmacenId={ctx.SubDisponibleId}"));
            var filaDisp = disp.GetProperty("items").EnumerateArray().Single();
            Assert.Equal(8m, filaDisp.GetProperty("cantidad").GetDecimal());
            Assert.Equal(8m, filaDisp.GetProperty("cantidadDisponible").GetDecimal());
        }, ContabTestKit.EmpresaBootstrapId);
    }

    [Fact]
    public async Task Recepcion_real_por_HTTP_con_cantidadDanada_manda_lo_danado_a_MAT_REV_y_aparece_en_la_bandeja()
    {
        var admin = await ContabTestKit.LoginAsync(_factory);
        await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            _ctxActual = ctx;
            try
            {
                using var resp = await admin.PostAsJsonAsync("/api/v1/almacen/recepciones", new
                {
                    ordenCompraId = ctx.OcId, fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                    cfdiUuidFiscal = "AD662D33-6934-459C-A128-BDF0393E0062",
                    lineas = new[]
                    {
                        new { articuloId = ctx.ArticuloId, lineaOcId = ctx.LineaOcId, cantidad = 10m, ubicacionId = ctx.RackId,
                              cantidadDanada = 2m, motivoDano = "Hojas rotas" },
                    },
                });
                Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());

                Assert.Equal(8m, await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId));
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
                var lista = await ContabTestKit.Json(await admin.GetAsync("/api/v1/almacen/revision"));
                var fila = lista.EnumerateArray().Single(x => x.GetProperty("ubicacionRevisionId").GetGuid() == ctx.RackRevisionId);
                Assert.Equal(2m, fila.GetProperty("cantidadPendiente").GetDecimal());
                Assert.Equal("Hojas rotas", fila.GetProperty("motivoDano").GetString());
            }
            finally
            {
                var ids = (await db.OutboxEntries.AsNoTracking().ToListAsync())
                    .Where(e => e.Payload.Contains(ctx.OcId.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Select(e => e.Id).ToArray();
                await db.OutboxEntries.Where(e => ids.Contains(e.Id)).ExecuteDeleteAsync();
            }
        }, ContabTestKit.EmpresaBootstrapId);
    }

    [Fact]
    public async Task Usuario_de_otra_sucursal_ve_0_filas_en_la_bandeja_y_recibe_403_al_dictaminar()
    {
        Operativo? ajeno = null;
        Operativo? propio = null;
        try
        {
            string[] permisos =
            [
                PermisosCanonicos.AlmacenEntradasLeer, PermisosCanonicos.AlmacenEntradasCapturar,
                PermisosCanonicos.AlmacenCuarentenaDictaminar,
            ];
            ajeno = await CrearOperativoAsync(SucursalOtra, permisos);
            propio = await CrearOperativoAsync(SucursalMid, permisos);
            await new CuarentenaRecepcionTests(_factory).EjecutarConFixtureAsync(async (db, ctx) =>
            {
                _ctxActual = ctx;
                await PonerAlmacenEnSucursalAsync(db, ctx.AlmacenId, SucursalMid);
                var lineaId = await RecibirConDanoAsync(db, ctx);

                // Control positivo: quien sí está asociado a la sucursal la ve.
                var propia = await ContabTestKit.Json(await propio.Client.GetAsync("/api/v1/almacen/revision"));
                Assert.Contains(propia.EnumerateArray(), x => x.GetProperty("lineaRevisionId").GetGuid() == lineaId);

                // La otra sucursal: ninguna fila de este almacén y 403 al dictaminar, con el saldo intacto.
                var lista = await ContabTestKit.Json(await ajeno.Client.GetAsync("/api/v1/almacen/revision?soloPendientes=false"));
                Assert.DoesNotContain(lista.EnumerateArray(), x => x.GetProperty("lineaRevisionId").GetGuid() == lineaId);
                using var dictamen = await ajeno.Client.PostAsJsonAsync($"/api/v1/almacen/revision/lineas/{lineaId}/dictamen", new
                {
                    resultado = 2, cantidad = 1m, motivo = "Intento", fechaMovimiento = Fecha.ToString("yyyy-MM-dd"),
                });
                Assert.Equal(HttpStatusCode.Forbidden, dictamen.StatusCode);
                Assert.Equal(2m, await LeerSaldoAsync(db, ctx.RackRevisionId, ctx.ArticuloId));
            }, ContabTestKit.EmpresaBootstrapId);
        }
        finally
        {
            await LimpiarAsync(ajeno);
            await LimpiarAsync(propio);
        }
    }
}
