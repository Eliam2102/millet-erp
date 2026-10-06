using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>Empresa fija para ejercitar mediator/puerto fuera de un request HTTP (sin tocar el JWT).</summary>
internal sealed class EmpresaDecorada(ICurrentEmpresaContext inner) : ICurrentEmpresaContext
{
    /// <summary>Empresa forzada para el código en proceso de la prueba; null = la del request (JWT). Los hosted services no la ven.</summary>
    public static readonly AsyncLocal<Guid?> Sobre = new();
    public Guid? Current => Sobre.Value ?? inner.Current;
    public bool IsBypassed => inner.IsBypassed;
    public IDisposable Bypass() => inner.Bypass();
}

/// <summary>Utilidades compartidas de las pruebas de Contabilidad (HTTP real + Postgres real). Datos FIX-*.</summary>
internal static class ContabTestKit
{
    public static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    public static AsyncLocal<Guid?> Sobre => EmpresaDecorada.Sobre;
    public const string Base = "/api/v1/contabilidad";

    /// <summary>Prefijo único por prueba. Lleva una letra para que el segmento no sea numérico (no cuenta en la jerarquía).</summary>
    public static string Sufijo() => "Z" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();

    public static string Codigo(string suf, string numerico) => $"FIX-{suf}-{numerico}";

    public static WebApplicationFactory<Program> ConEmpresa(this WebApplicationFactory<Program> f, Guid? empresaId, params (string Clave, string Valor)[] config) =>
        f.WithWebHostBuilder(b =>
        {
            foreach (var (k, v) in config) b.UseSetting(k, v);
            b.ConfigureTestServices(s =>
            {
                var original = s.Last(d => d.ServiceType == typeof(ICurrentEmpresaContext)).ImplementationType!;
                s.RemoveAll<ICurrentEmpresaContext>();
                s.AddScoped(original);
                s.AddScoped<ICurrentEmpresaContext>(sp => new EmpresaDecorada((ICurrentEmpresaContext)sp.GetRequiredService(original)));
            });
        }).Also(_ => Sobre.Value = empresaId);

    public static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    public static async Task<string> Code(HttpResponseMessage r) => (await Json(r)).GetProperty("code").GetString()!;

    public static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> f, string oid = "dev-superadmin", string? email = null, bool conIdempotencia = true)
    {
        var client = conIdempotencia ? f.CreateClientWithIdempotency() : f.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email ?? $"{oid}@dev.local", Nombre = oid, EmpresaId = (Guid?)null,
        });
        resp.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await Json(resp)).GetProperty("accessToken").GetString());
        return client;
    }

    /// <summary>Usuario nuevo con un rol que tiene SOLO los permisos indicados (molde de ClientesProteccionFiscalTests).</summary>
    public static async Task<HttpClient> ClienteConPermisosAsync(WebApplicationFactory<Program> f, params string[] codigos)
    {
        var admin = await LoginAsync(f);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"contab-test-{sufijo}";
        var email = $"{oid}@test.local";
        var client = f.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();
        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles",
            new { Id = Guid.Empty, Codigo = $"rol-contab-{sufijo}", Nombre = $"Rol Contab {sufijo}", Descripcion = (string?)null }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();
        return await LoginAsync(f, oid, email);
    }

    public static async Task<HttpResponseMessage> Send(HttpClient c, HttpMethod m, string url, object? body = null, string? ifMatch = null)
    {
        using var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body);
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await c.SendAsync(req);
    }

    public static string Etag(JsonElement cuenta) => $"\"{cuenta.GetProperty("version").GetInt32()}\"";

    public static async Task<JsonElement> CrearCuenta(HttpClient c, string codigo, string nombre = "FIX cuenta", Guid? padreId = null,
        string? naturaleza = "Deudora", string? tipo = "Afectable", string control = "Ninguna")
    {
        var r = await c.PostAsJsonAsync($"{Base}/cuentas", new { codigo, nombre, padreId, naturaleza, tipo, cuentaControl = control });
        Assert.Equal(System.Net.HttpStatusCode.Created, r.StatusCode);
        return await Json(r);
    }

    public static async Task<JsonElement> Obtener(HttpClient c, Guid id)
    {
        var r = await c.GetAsync($"{Base}/cuentas/{id}");
        r.EnsureSuccessStatusCode();
        return await Json(r);
    }

    /// <summary>
    /// F1-CON-03: confirmar o validar un movimiento exige su periodo abierto (falla cerrada). Crea el ejercicio de cada año de
    /// <paramref name="fechas"/> si falta y abre sus meses sin abrir. Las pruebas de periodos usan años lejanos propios y nunca
    /// cierran el año en curso, así que esto no interfiere con ellas.
    /// </summary>
    public static async Task AsegurarPeriodosAbiertosAsync(HttpClient admin, params DateOnly[] fechas)
    {
        foreach (var anio in fechas.Select(f => f.Year).Distinct())
        {
            var ejercicio = (await Json(await admin.GetAsync($"{Base}/periodos/ejercicios"))).EnumerateArray()
                .FirstOrDefault(e => e.GetProperty("anio").GetInt32() == anio);
            if (ejercicio.ValueKind == JsonValueKind.Undefined)
            {
                var creado = await admin.PostAsJsonAsync($"{Base}/periodos/ejercicios", new { anio });
                Assert.Equal(System.Net.HttpStatusCode.Created, creado.StatusCode);
                ejercicio = await Json(creado);
            }
            var porAbrir = ejercicio.GetProperty("periodos").EnumerateArray()
                .Where(p => p.GetProperty("numero").GetInt32() <= 12 && p.GetProperty("estado").GetString() == "NoAbierto")
                .Select(p => p.GetProperty("numero").GetInt32()).ToArray();
            if (porAbrir.Length > 0)
                (await Send(admin, HttpMethod.Post, $"{Base}/periodos/ejercicios/{ejercicio.GetProperty("id").GetGuid()}/abrir",
                    new { numeros = porAbrir, motivo = "FIX apertura para pruebas" }, Etag(ejercicio))).EnsureSuccessStatusCode();
        }
    }

    // ── Base de datos (SQL crudo: no depende de empresa ni de filtros) ──────

    public static async Task<long> Contar(IServiceProvider sp, string tabla, string? where = null)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM contabilidad.{tabla}" + (where is null ? string.Empty : $" WHERE {where}");
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<T?> Escalar<T>(IServiceProvider sp, string sql)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = sql;
        if (cmd.Connection!.State != System.Data.ConnectionState.Open) await cmd.Connection.OpenAsync();
        var v = await cmd.ExecuteScalarAsync();
        return v is null or DBNull ? default : (T)v;
    }

    /// <summary>Verifica que cada fila FIX tenga una sola creación central, atribuida y con snapshot de negocio.</summary>
    public static async Task AssertCreacionesAuditadasAsync(IServiceProvider sp, string tabla, string entidad,
        string condicion, string campoSnapshot, int esperado, string actorTipo = "usuario")
    {
        var filtro = $"""
            FROM core.audit_log a JOIN contabilidad.{tabla} r
                ON a.entidad_id = r.id AND a.empresa_id = r.empresa_id
            WHERE {condicion} AND a.modulo = 'Contabilidad' AND a.entidad = '{entidad}' AND a.operacion = 'crear'
            """;
        Assert.Equal(esperado, await Escalar<long>(sp, $"SELECT count(*) {filtro}"));
        Assert.Equal(esperado, await Escalar<long>(sp, $"""
            SELECT count(*) {filtro}
                AND a.empresa_id = '{EmpresaBootstrapId}' AND a.timestamp IS NOT NULL
                AND a.correlation_id IS NOT NULL AND a.actor_tipo = '{actorTipo}'
                AND nullif(a.actor_nombre, '') IS NOT NULL
                AND jsonb_typeof(a.cambios->'snapshot') = 'object'
                AND a.cambios->'snapshot' ? '{campoSnapshot}'
                {(actorTipo == "usuario" ? "AND a.usuario_id IS NOT NULL" : "")}
            """));
    }

    /// <summary>Borra todo lo creado por una prueba (por prefijo de código y fuente), de hojas a raíz (FK Restrict).</summary>
    public static async Task Limpiar(IServiceProvider sp, string sufijo)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ContabilidadDbContext>();
        var like = $"FIX-{sufijo}-%";
        await db.Database.ExecuteSqlRawAsync("UPDATE contabilidad.cuentas_contables SET rubro_id = NULL WHERE codigo LIKE {0}", like);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_uso WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", like);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables_origen WHERE cuenta_id IN (SELECT id FROM contabilidad.cuentas_contables WHERE codigo LIKE {0})", like);
        for (var nivel = 12; nivel >= 1; nivel--)
            await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.cuentas_contables WHERE codigo LIKE {0} AND nivel = {1}", like, nivel);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM contabilidad.importaciones_catalogo WHERE fuente LIKE {0}", $"FIX-{sufijo}%");
    }
}

internal static class Ext
{
    public static T Also<T>(this T x, Action<T> a) { a(x); return x; }
}
