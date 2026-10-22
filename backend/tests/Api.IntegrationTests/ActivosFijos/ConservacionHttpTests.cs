using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.ActivosFijos.Domain;
using Millet.ActivosFijos.Infrastructure.Persistence;
using Millet.ActivosFijos.Infrastructure.Workers;
using Millet.Api.IntegrationTests.ComercioExterior;
using Millet.Api.Seed;
using Millet.SharedKernel.Application;
namespace Millet.Api.IntegrationTests.ActivosFijos;
/// <summary>PostgreSQL desechable: tools/validate-integration-isolated.sh --activos-fijos. No usar una BD de desarrollo.</summary>
public sealed class ConservacionHttpTests
{
    private const string Base = "/api/v1/activos-fijos";
    private static readonly Guid ActivoId = DemoSesionSeedHostedService.Id("DEMO-AF-MAQ");
    private static async Task<HttpResponseMessage> Post(HttpClient c, string ruta, object datos, int? version = null, string? llave = null)
    {
        using var r = new HttpRequestMessage(HttpMethod.Post, Base + ruta) { Content = JsonContent.Create(datos) };
        r.Headers.Add("Idempotency-Key", llave ?? Guid.NewGuid().ToString()); if (version != null) r.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        return await c.SendAsync(r);
    }
    private static async Task<JsonElement> Json(HttpResponseMessage r)
    { r.EnsureSuccessStatusCode(); using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
    private static async Task<int> Version(HttpClient c, string ruta) => (await Json(await c.GetAsync(Base + ruta))).GetProperty("version").GetInt32();
    private static async Task<Guid> Archivo(HttpClient c, Guid id, int tipo)
    {
        using var body = new MultipartFormDataContent(); body.Add(new StringContent($"0000000f-0003-0000-0000-{tipo + 1:000000000000}"), "tipoDocumentoId");
        var archivo = new ByteArrayContent("%PDF-1.4\n% archivo sintético de prueba"u8.ToArray()); archivo.Headers.ContentType = new("application/pdf"); body.Add(archivo, "archivo", "prueba.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/activos/{id}/adjuntos") { Content = body };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return (await Json(await c.SendAsync(request))).GetProperty("id").GetGuid();
    }
    [Fact]
    public async Task Todos_los_recursos_rechazan_usuario_sin_permiso_y_sucursal_ajena()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync();
        using var ninguno = await e.ClienteAsync(false); using var ajeno = await e.ClienteAsync(true, asociar: false, permisosCe: false, permisosAf: true);
        using var propio = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var conteo = await Json(await Post(propio, "/conteos", new { sucursalId = e.SucursalId, ubicacion = "FIX VACIO" }));
        Assert.Equal(HttpStatusCode.OK, (await propio.GetAsync($"{Base}/por-vencer?sucursalId={e.SucursalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await propio.GetAsync($"{Base}/avisos?sucursalId={e.SucursalId}")).StatusCode);
        foreach (var c in new[] { ninguno, ajeno })
        {
            foreach (var ruta in new[] { $"/activos/{ActivoId}/documentos", $"/activos/{ActivoId}/mantenimientos", $"/activos/{ActivoId}/adjuntos", $"/activos/{ActivoId}/facturas-mantenimiento", $"/por-vencer?sucursalId={e.SucursalId}", $"/avisos?sucursalId={e.SucursalId}", $"/conteos?sucursalId={e.SucursalId}", $"/conteos/{conteo.GetProperty("id").GetGuid()}", $"/etiquetas?activoId={ActivoId}" })
                Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(Base + ruta)).StatusCode);
            foreach (var accion in new[] { "capturar", "cerrar", "autorizar" })
                Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/conteos/{conteo.GetProperty("id").GetGuid()}/{accion}", new { captura = new { numero = "EXTRA", resultado = 4, evidencia = "FIX evidencia" }, partidaId = Guid.NewGuid(), evidencia = "FIX autorización" }, 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/conteos", new { sucursalId = e.SucursalId }, 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/activos/{ActivoId}/documentos", new DocumentoActivoDatos(TipoDocumentoActivo.Seguro, Guid.NewGuid(), "F", "E", new(2026, 1, 1), null, null, null), 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, $"/activos/{ActivoId}/mantenimientos", new MantenimientoDatos(new(2026, 1, 1), TipoMantenimiento.Preventivo, Guid.NewGuid(), "Servicio", 10, null, null), 1)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "/etiquetas/registrar-impresion", new { activos = new[] { new { id = ActivoId, version = 1 } } })).StatusCode);
        }
    }
    [Fact]
    public async Task Lote_no_se_limita_a_pagina_y_registra_impresion_con_version_por_activo()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var c = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        Guid claseId;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass(); var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>();
            var clase = new ClaseActivo(Guid.NewGuid(), DemoSesionSeedHostedService.EmpresaId, "FIXET", "Clase de prueba de etiquetas", 10); claseId = clase.Id; db.Clases.Add(clase);
            for (var i = 0; i < 30; i++) db.Activos.Add(new Activo(Guid.NewGuid(), DemoSesionSeedHostedService.EmpresaId, $"FIX-SAP-{i:0000}", new("Equipo sintético", clase.Id, null, null, null, new(2026, 1, 1), null, 100, "MXN", 1, new(e.Responsable, e.SucursalId, "Nave", Guid.Empty, null), TipoImportacionActivo.No, null, false, "Factura sintética")));
            await db.SaveChangesAsync();
        }
        var lote = await Json(await c.GetAsync($"{Base}/etiquetas?sucursalId={e.SucursalId}&claseId={claseId}")); Assert.Equal(30, lote.GetArrayLength());
        var items = lote.EnumerateArray().Select(x => new { id = x.GetProperty("id").GetGuid(), version = x.GetProperty("version").GetInt32() }).ToArray();
        foreach (var x in lote.EnumerateArray()) { Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("empresa").GetString())); Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("sucursal").GetString())); }
        var llave = Guid.NewGuid().ToString(); Assert.Equal(HttpStatusCode.NoContent, (await Post(c, "/etiquetas/registrar-impresion", new { activos = items }, llave: llave)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post(c, "/etiquetas/registrar-impresion", new { activos = items }, llave: llave)).StatusCode);
        using var ver = e.Scope(); using var lectura = ver.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var activos = await ver.ServiceProvider.GetRequiredService<ActivosFijosDbContext>().Activos.Where(x => x.ClaseId == claseId).ToListAsync(); Assert.All(activos, a => Assert.NotNull(a.UltimaImpresionEn));
    }
    [Fact]
    public async Task Documentos_rechazan_seguro_sin_vigencia_y_conservan_archivos_y_revisiones_con_idempotencia()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var c = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var ruta = $"/activos/{ActivoId}"; var archivo = await Archivo(c, ActivoId, 1); var v = await Version(c, ruta);
        var d = new DocumentoActivoDatos(TipoDocumentoActivo.Seguro, archivo, "FIX-POLIZA", "Proveedor", new(2026, 1, 1), null, null, null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(c, ruta + "/documentos", d, v)).StatusCode);
        d = d with { VigenteDesde = new(2026, 1, 1), VigenteHasta = new(2027, 1, 1) };
        var llave = Guid.NewGuid().ToString(); var primero = await Json(await Post(c, ruta + "/documentos", d, v, llave));
        var repetido = await Json(await Post(c, ruta + "/documentos", d, v, llave)); Assert.Equal(primero.GetProperty("id").GetGuid(), repetido.GetProperty("id").GetGuid());
        var archivo2 = await Archivo(c, ActivoId, 1);
        var segundo = await Json(await Post(c, ruta + "/documentos", d with { AdjuntoId = archivo2, ReemplazaA = primero.GetProperty("id").GetGuid() }, await Version(c, ruta)));
        Assert.Equal(2, segundo.GetProperty("numeroVersion").GetInt32());
        var listado = await Json(await c.GetAsync(Base + ruta + "/documentos"));
        Assert.True(listado.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == primero.GetProperty("id").GetGuid()).GetProperty("reemplazado").GetBoolean());
        var archivos = await Json(await c.GetAsync(Base + ruta + "/adjuntos")); Assert.Contains(archivos.EnumerateArray(), x => x.GetProperty("id").GetGuid() == archivo);
        Assert.Equal((HttpStatusCode)428, (await Post(c, ruta + "/documentos", d)).StatusCode);
        Assert.False((await Post(c, ruta + "/documentos", d with { AdjuntoId = Guid.NewGuid() }, v)).IsSuccessStatusCode);
    }
    [Fact]
    public async Task Mantenimiento_con_factura_real_conserva_valor_y_rechaza_proveedor_ajeno()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var c = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        Guid facturaId; Guid proveedorId;
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext>();
            var maestros = scope.ServiceProvider.GetRequiredService<Millet.Compartido.Infrastructure.Persistence.CompartidoDbContext>();
            var proveedor = await maestros.Proveedores.SingleAsync(x => x.Clave == "DEMO-PROV-ACT");
            var fecha = new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
            var factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(DemoSesionSeedHostedService.EmpresaId, null, null, proveedor.Id, e.SucursalId, "FIX-MANT", null, fecha, fecha, new(2026, 11, 20), "MXN", 1, 2000, 0, 320, 0, 2320, "Servicio sintético de mantenimiento", fecha);
            cxp.FacturasProveedor.Add(factura); await cxp.SaveChangesAsync();
            facturaId = factura.Id; proveedorId = factura.ProveedorId;
        }
        var ruta = $"/activos/{ActivoId}"; var antes = await Json(await c.GetAsync(Base + ruta));
        var d = new MantenimientoDatos(new(2026, 10, 20), TipoMantenimiento.Preventivo, proveedorId, "FIX servicio", 2000, facturaId, new(2027, 1, 20));
        var registrado = await Json(await Post(c, ruta + "/mantenimientos", d, antes.GetProperty("version").GetInt32()));
        Assert.Equal(facturaId, registrado.GetProperty("facturaProveedorId").GetGuid());
        var despues = await Json(await c.GetAsync(Base + ruta)); Assert.Equal(antes.GetProperty("valorActual").GetDecimal(), despues.GetProperty("valorActual").GetDecimal());
        Assert.Equal(antes.GetProperty("historial").GetArrayLength(), despues.GetProperty("historial").GetArrayLength());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(c, ruta + "/mantenimientos", d with { ProveedorId = Guid.NewGuid() }, despues.GetProperty("version").GetInt32())).StatusCode);
        var historial = await Json(await c.GetAsync(Base + ruta + "/mantenimientos")); Assert.Contains(historial.EnumerateArray(), x => x.GetProperty("id").GetGuid() == registrado.GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task Worker_avisa_a_responsable_a_30_dias_sin_duplicar_y_emite_otro_al_vencer()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var scope = e.Scope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>(); var a = await db.Activos.SingleAsync(x => x.Id == ActivoId);
        var hoy = new DateOnly(2026, 10, 20);
        var d = new DocumentoActivo(a, new(TipoDocumentoActivo.Garantia, Guid.NewGuid(), "FIX-G", "Proveedor", hoy, hoy, hoy.AddDays(30), null)); db.Documentos.Add(d); await db.SaveChangesAsync();
        var opciones = new AvisosActivosOptions(); await AvisosActivosWorker.GenerarAsync(db, hoy, opciones, default); await AvisosActivosWorker.GenerarAsync(db, hoy, opciones, default);
        var aviso = Assert.Single(await db.Avisos.Where(x => x.FuenteId == d.Id).ToListAsync()); Assert.Equal(a.ResponsableId, aviso.ResponsableId); Assert.Equal(TipoAvisoActivo.PorVencer, aviso.Tipo);
        await AvisosActivosWorker.GenerarAsync(db, hoy.AddDays(30), opciones, default); await AvisosActivosWorker.GenerarAsync(db, hoy.AddDays(30), opciones, default);
        Assert.Equal(2, await db.Avisos.CountAsync(x => x.FuenteId == d.Id));
    }
    [Fact]
    public async Task Conteo_congela_tres_activos_reporta_cuatro_resultados_y_exige_otro_autorizador()
    {
        await using var e = await ExpedientesHttpTests.Escenario.CrearAsync(); using var c = await e.ClienteAsync(true, permisosCe: false, permisosAf: true); using var supervisor = await e.ClienteAsync(true, permisosCe: false, permisosAf: true);
        var ubicacion = "FIX-" + Guid.NewGuid(); var numeros = Enumerable.Range(1, 3).Select(i => $"FIX-{Guid.NewGuid():N}-{i}").ToArray();
        using (var scope = e.Scope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass(); var db = scope.ServiceProvider.GetRequiredService<ActivosFijosDbContext>(); var clase = await db.Clases.FirstAsync();
            foreach (var n in numeros) db.Activos.Add(new Activo(Guid.NewGuid(), DemoSesionSeedHostedService.EmpresaId, n, new("FIX Equipo", clase.Id, null, null, null, new(2026, 1, 1), null, 100, "MXN", 1, new(e.Responsable, e.SucursalId, ubicacion, Guid.Empty, null), TipoImportacionActivo.No, null, false, "FIX factura")));
            await db.SaveChangesAsync();
        }
        var abierto = await Json(await Post(c, "/conteos", new { sucursalId = e.SucursalId, ubicacion })); var ruta = "/conteos/" + abierto.GetProperty("id").GetGuid();
        Assert.Equal(3, abierto.GetProperty("partidas").GetArrayLength());
        for (var i = 0; i < 4; i++) await Json(await Post(c, ruta + "/capturar", new { captura = new { numero = i < 3 ? numeros[i] : "FIX-SOBRANTE", resultado = i + 1, ubicacionReal = "Nave B", evidencia = "FIX foto y revisión" } }, await Version(c, ruta)));
        var cerrado = await Json(await Post(c, ruta + "/cerrar", new { }, await Version(c, ruta))); var partidas = cerrado.GetProperty("partidas").EnumerateArray().ToArray();
        Assert.Equal(4, partidas.Length); Assert.Equal(4, partidas.Select(x => x.GetProperty("resultado").GetInt32()).Distinct().Count());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(c, ruta + "/capturar", new { captura = new { numero = numeros[0], resultado = 2, evidencia = "Cambiar" } }, await Version(c, ruta))).StatusCode);
        var diferencia = partidas.Single(x => x.GetProperty("resultado").GetInt32() == 2); var datos = new { partidaId = diferencia.GetProperty("id").GetGuid(), evidencia = "FIX revisado por supervisor" };
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, ruta + "/autorizar", datos, await Version(c, ruta))).StatusCode);
        var autorizado = await Json(await Post(supervisor, ruta + "/autorizar", datos, await Version(supervisor, ruta)));
        Assert.Equal("Baja", autorizado.GetProperty("partidas").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == datos.partidaId).GetProperty("propuesta").GetString());
    }
}
