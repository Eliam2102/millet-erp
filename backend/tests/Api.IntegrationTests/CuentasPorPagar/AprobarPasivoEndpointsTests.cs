using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth.Models;
using Millet.Api.IntegrationTests.P6;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.ComprobacionGastos;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Infrastructure.Outbox;
using Comprobacion = Millet.CuentasPorPagar.Domain.ComprobacionGastos.ComprobacionGastos;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.Api.IntegrationTests.CuentasPorPagar;

/// <summary>
/// P8.5 (86e3kdw2k): aprobar pasivo de caja chica sobre PostgreSQL desechable. Siembra por DbContext
/// y limpia en finally; el alcance por sucursal ajena lo cubre la teoría de escrituras de P6.
/// </summary>
public sealed class AprobarPasivoEndpointsTests(P6SucursalEndpointsFactory factory) : IClassFixture<P6SucursalEndpointsFactory>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Sucursal = Guid.Parse("00000005-0003-0000-0000-000000000001");
    private static readonly Guid Proveedor = Guid.Parse("00000005-0001-0000-0000-000000000001");
    private const string Base = "/api/v1/cuentas-por-pagar";

    private sealed record Semilla(Guid ComprobacionId, Guid FacturaId, Guid CfdiId, int Version);

    [Fact]
    public async Task Aprobar_aplica_emite_una_reposicion_y_un_evento_y_la_segunda_vez_es_422()
    {
        using var cliente = await LoginAsync("dev-superadmin");
        var s = await SembrarAsync();
        try
        {
            var r = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal((int)EstadoComprobacionGastos.Aplicada, (await Json(r)).GetProperty("estado").GetInt32());

            var db = Db(out var scope);
            using (scope)
            {
                var c = await db.ComprobacionesGastos.AsNoTracking().SingleAsync(x => x.Id == s.ComprobacionId);
                var repos = await db.ReposicionesCajaChica.AsNoTracking().Where(x => x.Id == c.ReposicionId).ToListAsync();
                var rep = Assert.Single(repos);
                Assert.Equal(1, rep.NumeroComprobaciones);
                Assert.Equal(c.MontoTotal, rep.MontoTotal);
                var outbox = await db.OutboxEntries.AsNoTracking().Select(x => x.Payload).ToListAsync();
                Assert.Single(outbox, p => p.Contains(rep.Id.ToString(), StringComparison.Ordinal));
            }

            var otra = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version + 1);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, otra.StatusCode);
            Assert.Contains("COMP_NO_APROBABLE_PASIVO", await otra.Content.ReadAsStringAsync());
        }
        finally { await LimpiarAsync(s); }
    }

    [Fact]
    public async Task Encabezados_version_y_permiso()
    {
        using var cliente = await LoginAsync("dev-superadmin");
        var s = await SembrarAsync();
        try
        {
            var sinVersion = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", null);
            Assert.Equal((HttpStatusCode)428, sinVersion.StatusCode);

            using var directo = await LoginAsync("dev-superadmin", conLlaveAutomatica: false);
            using var sinLlave = new HttpRequestMessage(HttpMethod.Post, $"{Base}/comprobaciones/{s.ComprobacionId}/aprobar-pasivo");
            sinLlave.Headers.Add("X-Expected-Version", s.Version.ToString());
            Assert.Equal(HttpStatusCode.BadRequest, (await directo.SendAsync(sinLlave)).StatusCode);

            var versionMala = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version + 7);
            Assert.Equal(HttpStatusCode.Conflict, versionMala.StatusCode);

            using var anonimo = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await Post(anonimo, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version)).StatusCode);

            var (sinPermiso, limpiar) = await LoginSinPermisosAsync();
            try { Assert.Equal(HttpStatusCode.Forbidden, (await Post(sinPermiso, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version)).StatusCode); }
            finally { await limpiar(); }
        }
        finally { await LimpiarAsync(s); }
    }

    [Fact]
    public async Task Rechazar_desde_pendiente_compensa_y_no_crea_reposicion()
    {
        using var cliente = await LoginAsync("dev-superadmin");
        var s = await SembrarAsync();
        try
        {
            var sinMotivo = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/rechazar", s.Version, new { motivo = "" });
            Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);

            const string motivo = "Ticket ilegible <b>ñ</b> & \"comillas\" 日本";
            var r = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/rechazar", s.Version, new { motivo });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal((int)EstadoComprobacionGastos.Rechazada, (await Json(r)).GetProperty("estado").GetInt32());

            var detalle = await Json(await cliente.GetAsync($"{Base}/comprobaciones/{s.ComprobacionId}"));
            Assert.Equal(motivo, detalle.GetProperty("motivoRechazo").GetString());

            var db = Db(out var scope);
            using (scope)
            {
                Assert.Equal(EstadoPasivo.Cancelada, (await db.FacturasProveedor.AsNoTracking().SingleAsync(f => f.Id == s.FacturaId)).Estado);
                Assert.Equal(EstadoCfdiRecibido.PorProcesar, (await db.CfdisRecibidos.AsNoTracking().SingleAsync(f => f.Id == s.CfdiId)).Estado);
                Assert.Null((await db.ComprobacionesGastos.AsNoTracking().SingleAsync(x => x.Id == s.ComprobacionId)).ReposicionId);
            }

            // Rechazada: ni aprobar ni volver a rechazar.
            Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version + 1)).StatusCode);
        }
        finally { await LimpiarAsync(s); }
    }

    [Fact]
    public async Task Autorizar_caja_chica_y_autorizar_su_factura_estan_cerrados_y_rechazar_aplicada_es_422()
    {
        using var cliente = await LoginAsync("dev-superadmin");
        var s = await SembrarAsync();
        try
        {
            var auth = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/autorizar", s.Version);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, auth.StatusCode);
            Assert.Contains("COMP_CAJA_CHICA_REQUIERE_APROBAR_PASIVO", await auth.Content.ReadAsStringAsync());

            int versionFactura;
            var db = Db(out var scope);
            using (scope) versionFactura = (await db.FacturasProveedor.AsNoTracking().SingleAsync(f => f.Id == s.FacturaId)).Version;
            var factura = await Post(cliente, $"/facturas/{s.FacturaId}/autorizar", versionFactura);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, factura.StatusCode);
            Assert.Contains("FACTURA_GASTO_INTERNO_NO_AUTORIZABLE", await factura.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, (await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aprobar-pasivo", s.Version)).StatusCode);
            Assert.Equal(HttpStatusCode.UnprocessableEntity,
                (await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/rechazar", s.Version + 1, new { motivo = "tarde" })).StatusCode);
        }
        finally { await LimpiarAsync(s); }
    }

    [Fact]
    public async Task Saldos_sin_montoMinimo_configuracion_no_existe_y_legada_Autorizada_se_aplica_con_una_reposicion()
    {
        using var cliente = await LoginAsync("dev-superadmin");
        var s = await SembrarAsync();
        try
        {
            var db = Db(out var scope);
            using (scope)
            {
                var c = await db.ComprobacionesGastos.SingleAsync(x => x.Id == s.ComprobacionId);
                // Fila legada: antes de P8.5 caja chica llegaba a Autorizada.
                db.Entry(c).Property("Estado").CurrentValue = EstadoComprobacionGastos.Autorizada;
                await db.SaveChangesAsync();
            }
            var version = (await Json(await cliente.GetAsync($"{Base}/comprobaciones/{s.ComprobacionId}"))).GetProperty("version").GetInt32();

            var aplicar = await Post(cliente, $"/comprobaciones/{s.ComprobacionId}/aplicar", version);
            Assert.Equal(HttpStatusCode.OK, aplicar.StatusCode);
            var db2 = Db(out var scope2);
            using (scope2)
            {
                var c = await db2.ComprobacionesGastos.AsNoTracking().SingleAsync(x => x.Id == s.ComprobacionId);
                var rep = await db2.ReposicionesCajaChica.AsNoTracking().SingleAsync(x => x.Id == c.ReposicionId);
                Assert.Equal(1, rep.NumeroComprobaciones);
            }

            var saldos = await (await cliente.GetAsync($"{Base}/reposiciones-caja/saldos")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("montoMinimo", saldos, StringComparison.OrdinalIgnoreCase);
            var put = await cliente.PutAsJsonAsync($"{Base}/reposiciones-caja/configuracion", new { sucursalId = Sucursal, montoMinimo = 1 });
            Assert.Contains(put.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        }
        finally { await LimpiarAsync(s); }
    }

    [Fact]
    public async Task Conteos_por_estado_coinciden_con_el_listado_y_muestran_el_estado_7()
    {
        using var cliente = await LoginAsync("dev-superadmin");
        var s = await SembrarAsync();
        try
        {
            var conteos = (await Json(await cliente.GetAsync($"{Base}/comprobaciones/conteos-por-estado?tipo={(int)TipoComprobacionGastos.ReembolsoCajaChica}")))
                .EnumerateArray().ToDictionary(x => x.GetProperty("estado").GetInt32(), x => x.GetProperty("total").GetInt32());
            Assert.True(conteos.GetValueOrDefault((int)EstadoComprobacionGastos.PendienteAprobarPasivo) >= 1);

            var lista = await Json(await cliente.GetAsync(
                $"{Base}/comprobaciones?tipo={(int)TipoComprobacionGastos.ReembolsoCajaChica}&estado={(int)EstadoComprobacionGastos.PendienteAprobarPasivo}&limit=200"));
            Assert.Equal(conteos[(int)EstadoComprobacionGastos.PendienteAprobarPasivo], lista.GetProperty("total").GetInt32());
        }
        finally { await LimpiarAsync(s); }
    }

    // ------------------------------------------------------------------ helpers

    private CuentasPorPagarDbContext Db(out IDisposable scope)
    {
        var s = factory.Services.CreateScope();
        var bypass = s.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        scope = new Liberar(s, bypass);
        return s.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>();
    }

    private sealed class Liberar(IServiceScope scope, IDisposable bypass) : IDisposable
    {
        public void Dispose() { bypass.Dispose(); scope.Dispose(); }
    }

    private async Task<Semilla> SembrarAsync()
    {
        var db = Db(out var scope);
        using (scope)
        {
            var ahora = DateTimeOffset.UtcNow;
            var n = Random.Shared.Next(100000, 999999);
            var cfdi = CfdiRecibido.Ingresar(Empresa, UuidCfdi.Parse(Guid.NewGuid().ToString()), RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"),
                TipoCfdi.Ingreso, $"AP-{n}", "AP", ahora, 116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, ahora, "ap-blob-de-prueba", null, "ap-hash-de-prueba");
            db.CfdisRecibidos.Add(cfdi);
            var factura = Factura.CapturarSinOc(Empresa, cfdi.Id, cfdi.UuidCfdi.Valor, Proveedor, Sucursal, $"AP-{n}", null,
                ahora, ahora, DateOnly.FromDateTime(ahora.DateTime).AddDays(30), "MXN", null, 100m, 0m, 16m, 0m, 116m, "Prueba aprobar pasivo", ahora);
            db.FacturasProveedor.Add(factura);
            var c = Comprobacion.Crear(Empresa, TipoComprobacionGastos.ReembolsoCajaChica, Sucursal, Guid.NewGuid(),
                DateOnly.FromDateTime(ahora.DateTime), DateOnly.FromDateTime(ahora.DateTime), "MXN", "Prueba aprobar pasivo", ahora,
                destinoReposicion: DestinoReposicionCaja.CuentaSucursal);
            var semilla = new Semilla(c.Id, factura.Id, cfdi.Id, 0);
            c.AgregarLinea(factura.Id, cfdi.Id, cfdi.UuidCfdi.Valor, Proveedor, factura.FolioProveedor, ahora, 100m, 16m, 0m, 116m, "MXN", "FIX");
            c.EnviarARevision(ahora);
            cfdi.MarcarConvertidoEnPasivo(factura.Id);
            db.ComprobacionesGastos.Add(c);
            await db.SaveChangesAsync();
            return semilla with { Version = c.Version };
        }
    }

    private async Task LimpiarAsync(Semilla s)
    {
        var db = Db(out var scope);
        using (scope)
        {
            var repId = await db.ComprobacionesGastos.Where(x => x.Id == s.ComprobacionId).Select(x => x.ReposicionId).SingleOrDefaultAsync();
            var refs = new[] { s.FacturaId.ToString(), repId?.ToString() ?? s.FacturaId.ToString() };
            var filas = await db.OutboxEntries.AsNoTracking().Select(x => new { x.Id, x.Payload }).ToListAsync();
            var ids = filas.Where(x => refs.Any(r => x.Payload.Contains(r, StringComparison.Ordinal))).Select(x => x.Id).ToArray();
            if (ids.Length > 0) await db.OutboxEntries.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            await db.ComprobacionesGastos.Where(x => x.Id == s.ComprobacionId).ExecuteDeleteAsync();
            if (repId is Guid r) await db.ReposicionesCajaChica.Where(x => x.Id == r).ExecuteDeleteAsync();
            await db.FacturasProveedor.Where(x => x.Id == s.FacturaId).ExecuteDeleteAsync();
            await db.CfdisRecibidos.Where(x => x.Id == s.CfdiId).ExecuteDeleteAsync();
        }
    }

    private static async Task<HttpResponseMessage> Post(HttpClient cliente, string ruta, int? version, object? cuerpo = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Base + ruta);
        req.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        if (version is int v) req.Headers.Add("X-Expected-Version", v.ToString());
        if (cuerpo is not null) req.Content = JsonContent.Create(cuerpo);
        return await cliente.SendAsync(req);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(texto).RootElement.Clone();
    }

    private async Task<HttpClient> LoginAsync(string oid, bool conLlaveAutomatica = true)
    {
        var client = conLlaveAutomatica ? factory.CreateClientWithIdempotency() : factory.CreateClient();
        var r = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = oid == "dev-superadmin" ? "superadmin@dev.local" : $"{oid}@test.local", Nombre = oid });
        r.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await r.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        return client;
    }

    /// <summary>Usuario con rol vacío (sin ningún permiso) para el 403.</summary>
    private async Task<(HttpClient, Func<Task>)> LoginSinPermisosAsync()
    {
        var usuarioId = Guid.CreateVersion7(); var rolId = Guid.CreateVersion7(); var oid = $"ap-sin-permisos-{usuarioId:N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            db.Roles.Add(new Rol(rolId, oid, "Sin permisos AP"));
            db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Sin permisos AP"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, rolId, null));
            db.UsuarioSucursales.Add(new UsuarioSucursal(Guid.CreateVersion7(), usuarioId, Sucursal, Empresa));
            await db.SaveChangesAsync();
        }
        var cliente = await LoginAsync(oid);
        return (cliente, async () =>
        {
            cliente.Dispose();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await db.UsuarioSucursales.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await db.UsuarioEmpresaRoles.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await db.UsuarioPreferencias.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await db.Usuarios.Where(x => x.Id == usuarioId).ExecuteDeleteAsync();
            await db.Roles.Where(x => x.Id == rolId).ExecuteDeleteAsync();
        });
    }
}
