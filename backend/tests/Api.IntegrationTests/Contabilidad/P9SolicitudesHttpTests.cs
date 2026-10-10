using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;
using Fix = Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>Contrato P9 real: HTTP 202, catálogo vigente, DAF, segregación y bitácora; sin autoautorizar respuestas.</summary>
public sealed class P9SolicitudesHttpTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly List<HttpClient> _clientes = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        foreach (var c in _clientes) await LimpiarClienteAsync(c);
    }
    private async Task<HttpClient> Cliente(params string[] permisos)
    {
        var c = await ClienteConPermisosAsync(factory, permisos);
        _clientes.Add(c);
        return c;
    }
    private Task<HttpClient> Contador() => Cliente(
        PermisosCanonicos.ContabilidadCatalogoLeer, PermisosCanonicos.ContabilidadCatalogoAdministrar,
        PermisosCanonicos.ContabilidadCatalogoImportar);
    private Task<HttpClient> Daf() => Cliente(
        PermisosCanonicos.ContabilidadCatalogoLeer, PermisosCanonicos.ContabilidadCatalogoAutorizar);

    private static async Task<JsonElement> Pendiente(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        return await Json(r);
    }

    private static async Task<HttpResponseMessage> Resolver(HttpClient c, Guid id, bool autorizar = true,
        string? motivo = null, string? version = "\"1\"")
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/solicitudes/{id}/resolver")
        { Content = JsonContent.Create(new { autorizar, motivo }) };
        if (version is not null) req.Headers.TryAddWithoutValidation("If-Match", version);
        return await c.SendAsync(req);
    }

    private static async Task<JsonElement> Cambiar(HttpClient c, JsonElement cuenta, string nombre, bool marca = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, $"{Base}/cuentas/{cuenta.GetProperty("id").GetGuid()}")
        { Content = JsonContent.Create(new { nombre, padreId = cuenta.GetProperty("padreId"), naturaleza = "Deudora",
            tipo = "Afectable", cuentaControl = "Ninguna", noAfectableManual = marca }) };
        req.Headers.TryAddWithoutValidation("If-Match", Etag(cuenta));
        return await Pendiente(await c.SendAsync(req));
    }

    [Fact]
    public async Task Alta_cambio_baja_y_reactivacion_solo_se_aplican_al_autorizar_y_dejan_bitacora()
    {
        var suf = Sufijo();
        try
        {
            var contador = await Contador();
            var daf = await Daf();
            var alta = await Pendiente(await contador.PostAsJsonAsync($"{Base}/cuentas", new {
                codigo = Codigo(suf, "1"), nombre = "FIX inicial", naturaleza = "Deudora", cuentaControl = "Ninguna" }));
            var id = alta.GetProperty("id").GetGuid();
            var sid = alta.GetProperty("solicitudId").GetGuid();
            Assert.Equal(HttpStatusCode.NotFound, (await contador.GetAsync($"{Base}/cuentas/{id}")).StatusCode);
            var solicitud = await Json(await daf.GetAsync($"{Base}/solicitudes/{sid}"));
            Assert.Equal("Pendiente", solicitud.GetProperty("estado").GetString());
            Assert.Equal(JsonValueKind.Null, solicitud.GetProperty("cambios")[0].GetProperty("antes").ValueKind);
            Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, sid)).StatusCode);
            var vigente = await Obtener(contador, id);
            var cambio = await Cambiar(contador, vigente, "FIX aprobado", true);
            var cambioId = cambio.GetProperty("solicitudId").GetGuid();
            Assert.Equal("FIX inicial", (await Obtener(contador, id)).GetProperty("nombre").GetString());
            solicitud = await Json(await daf.GetAsync($"{Base}/solicitudes/{cambioId}"));
            Assert.Equal("FIX inicial", solicitud.GetProperty("cambios")[0].GetProperty("antes").GetProperty("nombre").GetString());
            Assert.Equal("FIX aprobado", solicitud.GetProperty("cambios")[0].GetProperty("despues").GetProperty("nombre").GetString());
            Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, cambioId)).StatusCode);
            vigente = await Obtener(contador, id);
            Assert.Equal("FIX aprobado", vigente.GetProperty("nombre").GetString());
            Assert.True(vigente.GetProperty("noAfectableManual").GetBoolean());
            foreach (var accion in new[] { "desactivar", "reactivar" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/cuentas/{id}/{accion}");
                req.Headers.TryAddWithoutValidation("If-Match", Etag(vigente));
                var propuesta = await Pendiente(await contador.SendAsync(req));
                Assert.Equal(accion == "desactivar", (await Obtener(contador, id)).GetProperty("activa").GetBoolean());
                Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, propuesta.GetProperty("solicitudId").GetGuid())).StatusCode);
                vigente = await Obtener(contador, id);
                Assert.Equal(accion == "reactivar", vigente.GetProperty("activa").GetBoolean());
            }
            Assert.Equal(2, await Escalar<long>(factory.Services,
                $"SELECT count(*) FROM core.audit_log WHERE entidad = 'SolicitudCatalogo' AND entidad_id = '{cambioId}'"));
            Assert.Equal(1, await Escalar<long>(factory.Services,
                $"SELECT count(*) FROM contabilidad.solicitudes_catalogo WHERE id = '{cambioId}' AND estado = 'Autorizada' AND preparada_por_id <> resuelta_por_id AND resuelta_en IS NOT NULL"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Sin_permiso_403_y_autor_superadmin_422_rechazo_conserva_motivo_sin_aplicar()
    {
        var suf = Sufijo();
        try
        {
            var admin = await LoginAsync(factory);
            var contador = await Contador();
            var daf = await Daf();
            var cuenta = await CrearCuenta(admin, Codigo(suf, "1"));
            var cambio = await Cambiar(admin, cuenta, "FIX rechazado");
            var sid = cambio.GetProperty("solicitudId").GetGuid();
            Assert.Equal(HttpStatusCode.Forbidden, (await Resolver(contador, sid)).StatusCode);
            var propia = await Resolver(admin, sid);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, propia.StatusCode);
            Assert.Equal("CONTAB_SOLICITUD_MISMO_AUTOR", await Code(propia));
            Assert.Equal((HttpStatusCode)428, (await Resolver(daf, sid, version: null)).StatusCode);
            var rechazo = await Resolver(daf, sid, false, "  FIX corregir naturaleza  ");
            Assert.Equal(HttpStatusCode.OK, rechazo.StatusCode);
            var s = await Json(rechazo);
            Assert.Equal("Rechazada", s.GetProperty("estado").GetString());
            Assert.Equal("FIX corregir naturaleza", s.GetProperty("motivoRechazo").GetString());
            Assert.Equal(cuenta.GetProperty("nombre").GetString(), (await Obtener(admin, cuenta.GetProperty("id").GetGuid())).GetProperty("nombre").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await Resolver(daf, sid)).StatusCode);
            var lista = await Json(await daf.GetAsync($"{Base}/solicitudes?estado=Rechazada&limit=100"));
            Assert.Contains(lista.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == sid);
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Importacion_pendiente_unica_por_lote_autorizada_valida_la_marca_manual_y_limpia_datos()
    {
        var suf = Sufijo();
        try
        {
            var contador = await Contador();
            var daf = await Daf();
            var csv = "codigo;nombre;naturaleza;no_afectable_manual\n"
                + $"{Codigo(suf, "100.00.00.00")};FIX raíz;Deudora;No\n"
                + $"{Codigo(suf, "100.10.00.00")};FIX banco;Deudora;Sí\n"
                + $"{Codigo(suf, "100.20.00.00")};FIX gasto;Deudora;No\n";
            var req = Fix.Request(csv, $"FIX-{suf}");
            var vista = await Json(await contador.PostAsJsonAsync($"{Base}/importaciones/vista-previa", req));
            Assert.True(vista.GetProperty("puedeAplicar").GetBoolean(), vista.GetRawText());
            var propuesta = await Pendiente(await contador.PostAsJsonAsync($"{Base}/importaciones", req));
            var sid = propuesta.GetProperty("solicitudId").GetGuid();
            var repetida = await Pendiente(await contador.PostAsJsonAsync($"{Base}/importaciones", req));
            Assert.Equal(sid, repetida.GetProperty("solicitudId").GetGuid());
            Assert.Equal(0, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
            Assert.Equal(0, await Contar(factory.Services, "importaciones_catalogo", $"fuente = 'FIX-{suf}'"));
            Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, sid)).StatusCode);
            Assert.Equal(3, await Contar(factory.Services, "cuentas_contables", $"codigo LIKE 'FIX-{suf}-%'"));
            Assert.Equal(1, await Contar(factory.Services, "importaciones_catalogo", $"fuente = 'FIX-{suf}'"));
            foreach (var (codigo, valida) in new[] { ("100.10.00.00", false), ("100.20.00.00", true) })
            {
                var v = await Json(await contador.PostAsJsonAsync($"{Base}/cuentas/validar-movimiento", new { codigo = Codigo(suf, codigo), origen = "Manual" }));
                Assert.Equal(valida, v.GetProperty("valida").GetBoolean());
                if (!valida) Assert.Equal("CUENTA_NO_AFECTABLE_MANUAL", v.GetProperty("codigo").GetString());
            }
            var idempotente = await contador.PostAsJsonAsync($"{Base}/importaciones", req);
            Assert.Equal(HttpStatusCode.OK, idempotente.StatusCode);
            Assert.True((await Json(idempotente)).GetProperty("idempotente").GetBoolean());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Rechazar_alta_libera_el_codigo_pendiente_y_permite_preparar_otra()
    {
        var suf = Sufijo();
        try
        {
            var contador = await Contador();
            var daf = await Daf();
            var cuerpo = new { codigo = Codigo(suf, "1"), nombre = "FIX propuesta", naturaleza = "Deudora", cuentaControl = "Ninguna" };
            var primera = await Pendiente(await contador.PostAsJsonAsync($"{Base}/cuentas", cuerpo));
            var sid = primera.GetProperty("solicitudId").GetGuid();
            var duplicada = await contador.PostAsJsonAsync($"{Base}/cuentas", cuerpo);
            Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
            Assert.Equal("CONTAB_CUENTA_CODIGO_DUPLICADO", await Code(duplicada));
            Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, sid, false, "FIX corregir propuesta")).StatusCode);
            var segunda = await Pendiente(await contador.PostAsJsonAsync($"{Base}/cuentas", cuerpo));
            Assert.NotEqual(sid, segunda.GetProperty("solicitudId").GetGuid());
            Assert.Equal(1, await Contar(factory.Services, "solicitudes_catalogo", $"codigo_alta = '{cuerpo.codigo}' AND estado = 'Pendiente'"));
            Assert.Equal(0, await Contar(factory.Services, "cuentas_contables", $"codigo = '{cuerpo.codigo}'"));
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Catalogo_cambiado_devuelve_conflicto_y_conserva_la_solicitud_pendiente()
    {
        var suf = Sufijo();
        try
        {
            var c = await Contador();
            var daf = await Daf();
            var cuenta = await CrearCuenta(c, Codigo(suf, "1"));
            var a = await Cambiar(c, cuenta, "FIX primera");
            var b = await Cambiar(c, cuenta, "FIX segunda");
            Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, a.GetProperty("solicitudId").GetGuid())).StatusCode);
            var sid = b.GetProperty("solicitudId").GetGuid();
            var conflicto = await Resolver(daf, sid);
            Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.Equal("CONTAB_SOLICITUD_CATALOGO_CAMBIO", await Code(conflicto));
            Assert.Equal("Pendiente", (await Json(await c.GetAsync($"{Base}/solicitudes/{sid}"))).GetProperty("estado").GetString());
        }
        finally { await Limpiar(factory.Services, suf); }
    }

    [Fact]
    public async Task Movimiento_manual_marcado_rechaza_422_y_no_marcado_se_confirma_201()
    {
        var suf = Sufijo();
        Guid? sucursalCreada = null;
        try
        {
            var admin = await LoginAsync(factory);
            var daf = await Daf();
            var hoy = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("America/Merida")).DateTime);
            await AsegurarPeriodosAbiertosAsync(admin, hoy);
            var raiz = await CrearCuenta(admin, Codigo(suf, "1"));
            var banco = await CrearCuenta(admin, Codigo(suf, "1.1"), padreId: raiz.GetProperty("id").GetGuid());
            var gasto = await CrearCuenta(admin, Codigo(suf, "1.2"), padreId: raiz.GetProperty("id").GetGuid());
            var cambio = await Cambiar(admin, banco, "FIX banco protegido", true);
            Assert.Equal(HttpStatusCode.OK, (await Resolver(daf, cambio.GetProperty("solicitudId").GetGuid())).StatusCode);
            var tipo = await Json(await admin.PostAsJsonAsync($"{Base}/tipos-documento",
                new { clave = $"FIX-{suf}", nombre = "FIX prueba manual P9", esPrueba = true }));
            var sucursal = await admin.PostAsJsonAsync("/api/v1/admin/empresas/sucursales",
                new { Id = Guid.Empty, Clave = $"P9{suf}", Nombre = $"FIX Sucursal P9 {suf}" });
            sucursal.EnsureSuccessStatusCode();
            var sucursalId = (await Json(sucursal)).GetProperty("id").GetGuid();
            sucursalCreada = sucursalId;
            object Movimiento(JsonElement cuenta) => new { cuentaId = cuenta.GetProperty("id").GetGuid(),
                tipoDocumentoId = tipo.GetProperty("id").GetGuid(), fechaContable = hoy, sucursalId,
                origen = "Manual", referencia = $"FIX-{suf}" };
            var rechazo = await admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Movimiento(banco));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rechazo.StatusCode);
            Assert.Contains((await Json(rechazo)).GetProperty("errores").EnumerateArray(),
                e => e.GetProperty("codigo").GetString() == "CUENTA_NO_AFECTABLE_MANUAL");
            Assert.Equal(0, await Contar(factory.Services, "movimientos_dimension_prueba", $"referencia = 'FIX-{suf}'"));
            Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"{Base}/movimientos-prueba", Movimiento(gasto))).StatusCode);
            Assert.Equal(1, await Contar(factory.Services, "movimientos_dimension_prueba", $"referencia = 'FIX-{suf}'"));
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contabilidad.movimientos_dimension_prueba WHERE referencia = {$"FIX-{suf}"}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contabilidad.tipos_documento_contable WHERE clave = {$"FIX-{suf}"}");
            await Limpiar(factory.Services, suf);
            if (sucursalCreada is { } sucursalId)
                await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Database
                    .ExecuteSqlInterpolatedAsync($"DELETE FROM compartido.sucursales WHERE id = {sucursalId}");
        }
    }

    [Fact]
    public async Task Seed_DAF_tiene_permiso_de_autorizacion()
    {
        Assert.Equal(1, await Escalar<long>(factory.Services, """
            SELECT count(*) FROM identidad.roles r JOIN identidad.rol_permisos rp ON rp.rol_id = r.id
            JOIN identidad.permisos p ON p.id = rp.permiso_id
            WHERE r.codigo = 'direccion-administracion-finanzas' AND p.codigo = 'contabilidad.catalogo.autorizar'
            """));
    }
}
