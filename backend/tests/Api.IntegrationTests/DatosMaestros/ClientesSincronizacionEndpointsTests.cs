using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Domain;
using Millet.Integraciones.Aw.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.DatosMaestros;

/// <summary>
/// ADM-06 Entrega D2: endpoints de sincronización de clientes A+W contra PostgreSQL real.
/// El origen es un fake propio por prueba y el dispatcher real ejecuta los barridos encolados.
/// Comparte colección con <c>AwClientesSincronizadorTests</c>: ambas tocan el cupo de un barrido
/// vivo por origen y no deben correr en paralelo.
/// </summary>
[Collection("AwClientesSync")]
public class ClientesSincronizacionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/datos-maestros/clientes/sincronizacion";
    private const string Sentinel = "SENTINEL-TEXTO-DE-EXCEPCION";
    private const string ConnSentinel = "Server=sentinel-host;Password=Sup3rS3cret";
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static int _siguienteBase = Random.Shared.Next(1_000_000, 900_000_000);

    private readonly WebApplicationFactory<Program> _factory;

    public ClientesSincronizacionEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private static int NuevaBase() => Interlocked.Add(ref _siguienteBase, 10_000);

    private static AwClienteOrigenFila Fila(int id, string? name1 = null) => new(
        id, 1, name1 ?? $"CLIENTE DEMO {id}", null, null, "CALLE DEMO 1", "CIUDAD DEMO", "06600", "PROV", "MX",
        null, null, "5550001", null, null, "ZDEMO", "MXN", 1000m, 500m, 30d, null, 0, null, null,
        [new AwCondicionCoincidencia(30, 30)]);

    private sealed class OrigenFake(List<AwClienteOrigenFila> filas) : IAwClientesOrigen
    {
        public SemaphoreSlim? Gate { get; init; }
        public Exception? Error { get; init; }

        public Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
        {
            if (Error is not null) throw Error;
            return Task.FromResult(filas.FirstOrDefault(f => f.Id.ToString() == referencia));
        }

        public async Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
        {
            if (Gate is not null) await Gate.WaitAsync(ct);
            if (Error is not null) throw Error;
            var desde = cursor is null ? 0 : int.Parse(cursor);
            var pag = filas.Where(f => f.Id > desde).OrderBy(f => f.Id).Take(tamano).ToList();
            return new AwClientesPagina(pag, pag.Count == tamano ? pag[^1].Id.ToString() : null);
        }
    }

    /// <summary>Host con flags de clientes encendidos (o no) y el origen fake sustituido.</summary>
    private WebApplicationFactory<Program> Host(OrigenFake? origen, bool flags = true) =>
        _factory.WithWebHostBuilder(b =>
        {
            // UseSetting (no ConfigureAppConfiguration): Program lee la config al construir el builder.
            b.UseSetting("IntegracionesAw:Clientes:LecturaHabilitada", flags ? "true" : "false");
            b.UseSetting("IntegracionesAw:Clientes:AplicacionHabilitada", flags ? "true" : "false");
            b.UseSetting("IntegracionesAw:Clientes:TamanoLote", "3");
            b.UseSetting("IntegracionesAw:Clientes:MapeoMoneda:MXN", "MXN"); // sin mapeo la moneda no se asume y el alta se omite
            if (origen is not null)
                b.ConfigureServices(s => s.AddSingleton<IAwClientesOrigen>(origen));
        });

    private static async Task LimpiarVivosAsync(WebApplicationFactory<Program> f)
    {
        using var scope = f.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IntegracionesAwDbContext>().ClientesEjecuciones
            .Where(e => e.Estado == AwClientesEjecucionEstado.Pendiente || e.Estado == AwClientesEjecucionEstado.EnCurso)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, AwClientesEjecucionEstado.Cancelada));
    }

    private static Task<HttpResponseMessage> PostBarrido(HttpClient c) =>
        c.PostAsJsonAsync($"{Base}/ejecuciones", new { tipo = "Barrido" });

    private static async Task<JsonElement> EsperarFinAsync(HttpClient c, Guid id)
    {
        var limite = DateTime.UtcNow.AddSeconds(40);
        while (true)
        {
            var d = await Json(await c.GetAsync($"{Base}/ejecuciones/{id}"));
            var estado = d.GetProperty("ejecucion").GetProperty("estado").GetString();
            if (estado is not ("Pendiente" or "EnCurso")) return d;
            Assert.True(DateTime.UtcNow < limite, $"La ejecución {id} no terminó (estado {estado}).");
            await Task.Delay(500);
        }
    }

    [Fact]
    public async Task Post_sin_Idempotency_Key_da_400()
    {
        await using var f = Host(new OrigenFake([]));
        var conKey = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);
        var sinKey = f.CreateDefaultClient();
        sinKey.DefaultRequestHeaders.Authorization = conKey.DefaultRequestHeaders.Authorization;

        var resp = await PostBarrido(sinKey);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Sin_permiso_sincronizar_da_403_en_los_cuatro_endpoints()
    {
        await using var f = Host(new OrigenFake([]));
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesGestionar);

        Assert.Equal(HttpStatusCode.Forbidden, (await PostBarrido(c)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/ejecuciones")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync($"{Base}/ejecuciones/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = "1" })).StatusCode);
    }

    [Fact]
    public async Task Con_permiso_pero_flags_apagados_da_503()
    {
        await using var f = Host(null, flags: false);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);

        var post = await PostBarrido(c);
        var reintento = await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = "1" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, post.StatusCode);
        Assert.Equal("AW_CLIENTES_LECTURA_DESHABILITADA", (await ReadJson(post)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, reintento.StatusCode);
    }

    [Fact]
    public async Task Barrido_se_encola_202_y_el_dispatcher_lo_completa_con_conflicto_listado()
    {
        var b = NuevaBase();
        var filas = new List<AwClienteOrigenFila> { Fila(b + 1), Fila(b + 2), Fila(b + 3), Fila(b + 4) };
        await using var f = Host(new OrigenFake(filas));
        await LimpiarVivosAsync(f);
        // Cliente manual con la misma referencia externa que la fila b+2 => conflicto trazable.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            db.Clientes.Add(new Cliente(Guid.CreateVersion7(), $"M-{b + 2}", "CLIENTE MANUAL", referenciaExterna: (b + 2).ToString()));
            await db.SaveChangesAsync();
        }
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);

        var resp = await PostBarrido(c);

        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);
        var body = await ReadJson(resp);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("Pendiente", body.GetProperty("estado").GetString());
        Assert.EndsWith($"{Base}/ejecuciones/{id}", resp.Headers.Location!.ToString());

        var d = await EsperarFinAsync(c, id);
        var e = d.GetProperty("ejecucion");
        Assert.Equal("Completa", e.GetProperty("estado").GetString());
        Assert.Equal(4, e.GetProperty("leidos").GetInt32());
        Assert.Equal(3, e.GetProperty("creados").GetInt32());
        Assert.Equal(1, e.GetProperty("conflictos").GetInt32());
        var err = Assert.Single(d.GetProperty("errores").EnumerateArray());
        Assert.Equal((b + 2).ToString(), err.GetProperty("referencia").GetString());
        Assert.Equal("conflicto_correlacion", err.GetProperty("codigo").GetString());

        var lista = await Json(await c.GetAsync($"{Base}/ejecuciones?estado=Completa&limit=50"));
        Assert.Contains(lista.GetProperty("items").EnumerateArray(), i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"{Base}/ejecuciones/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Segundo_POST_con_barrido_vivo_da_409()
    {
        var gate = new SemaphoreSlim(0);
        await using var f = Host(new OrigenFake([Fila(NuevaBase() + 1)]) { Gate = gate });
        await LimpiarVivosAsync(f);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);

        var primero = await PostBarrido(c);
        var segundo = await PostBarrido(c);

        Assert.Equal(HttpStatusCode.Accepted, primero.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segundo.StatusCode);
        Assert.Equal("AW_CLIENTES_BARRIDO_EN_CURSO", (await ReadJson(segundo)).GetProperty("code").GetString());

        gate.Release(10);
        await EsperarFinAsync(c, (await ReadJson(primero)).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Reintento_inline_devuelve_estado_real_y_referencia_invalida_da_422()
    {
        var b = NuevaBase();
        await using var f = Host(new OrigenFake([Fila(b + 1)]));
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);

        var ok = await Json(await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = (b + 1).ToString() }));
        Assert.Equal("Completa", ok.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal("Referencia", ok.GetProperty("ejecucion").GetProperty("tipo").GetString());
        Assert.Equal(1, ok.GetProperty("ejecucion").GetProperty("creados").GetInt32());

        var ausente = await Json(await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = (b + 9).ToString() }));
        Assert.Equal("Parcial", ausente.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal("no_encontrada_en_origen", ausente.GetProperty("errores")[0].GetProperty("codigo").GetString());

        var invalida = await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = "  " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalida.StatusCode);
        Assert.Equal("AW_CLIENTES_REFERENCIA_INVALIDA", (await ReadJson(invalida)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Errores_no_exponen_texto_de_excepcion_ni_connection_string()
    {
        var b = NuevaBase();
        var origen = new OrigenFake([Fila(b + 1), Fila(b + 2, name1: new string('X', 300))])
        {
            Error = new InvalidOperationException($"{Sentinel} {ConnSentinel}"),
        };
        await using var f = Host(origen);
        await LimpiarVivosAsync(f);
        var c = await ClienteConPermisosAsync(f, PermisosCanonicos.DatosMaestrosClientesSincronizar);

        // 1) Barrido: el origen lanza al leer la página => Fallida con código + tipo, sin texto.
        var id = (await ReadJson(await PostBarrido(c))).GetProperty("id").GetGuid();
        var d = await EsperarFinAsync(c, id);
        Assert.Equal("Fallida", d.GetProperty("ejecucion").GetProperty("estado").GetString());
        Assert.Equal("lectura_origen_fallida (InvalidOperationException)",
            d.GetProperty("ejecucion").GetProperty("errorGeneral").GetString());

        // 2) Reintento con el origen roto también sale Fallida sin texto.
        var r = await Json(await c.PostAsJsonAsync($"{Base}/reintentos", new { referencia = (b + 1).ToString() }));
        Assert.Equal("Fallida", r.GetProperty("ejecucion").GetProperty("estado").GetString());

        // 3) Fila cuya aplicación revienta (razón social de 300 chars): mensaje sin texto de la BD.
        var sano = Host(new OrigenFake([Fila(b + 5, name1: new string('X', 300))]));
        await using (sano)
        {
            var c2 = await ClienteConPermisosAsync(sano, PermisosCanonicos.DatosMaestrosClientesSincronizar);
            var r2 = await Json(await c2.PostAsJsonAsync($"{Base}/reintentos", new { referencia = (b + 5).ToString() }));
            var e = Assert.Single(r2.GetProperty("errores").EnumerateArray());
            Assert.Equal("aplicacion_fallida", e.GetProperty("codigo").GetString());
            Assert.Matches(@"^No se pudo aplicar el cliente \(\w+\)$", e.GetProperty("mensaje").GetString());
            Assert.DoesNotContain("character varying", r2.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        foreach (var texto in new[] { d.ToString(), r.ToString() })
        {
            Assert.DoesNotContain(Sentinel, texto);
            Assert.DoesNotContain("Sup3rS3cret", texto);
            Assert.DoesNotContain("sentinel-host", texto);
        }
    }

    // --- Helpers (duplicados a propósito, como en ClientesOrigenAwLecturaTests) ---

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r)
    {
        using var doc = await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r)
    {
        r.EnsureSuccessStatusCode();
        return await ReadJson(r);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> f, string oid, string email)
    {
        var client = f.CreateClientWithIdempotency();
        var resp = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        });
        var j = await Json(resp);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", j.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<HttpClient> ClienteConPermisosAsync(WebApplicationFactory<Program> f, params string[] codigos)
    {
        var admin = await LoginAsync(f, "dev-superadmin", "superadmin@dev.local");
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var oid = $"sync-test-{sufijo}";
        var email = $"sync-test-{sufijo}@test.local";

        var client = f.CreateClientWithIdempotency();
        var primer = await Json(await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = email, Nombre = oid, EmpresaId = (Guid?)null,
        }));
        var usuarioId = primer.GetProperty("usuario").GetProperty("id").GetGuid();

        var rolId = (await Json(await admin.PostAsJsonAsync("/api/v1/identidad/roles", new
        {
            Id = Guid.Empty, Codigo = $"rol-sync-{sufijo}", Nombre = $"Rol Sync {sufijo}", Descripcion = (string?)null,
        }))).GetProperty("id").GetGuid();
        var ids = codigos.Select(c => PermisosCanonicos.Todos.First(p => p.Codigo == c).Id).ToArray();
        (await admin.PutAsJsonAsync($"/api/v1/identidad/roles/{rolId}/permisos", new { PermisoIds = ids }))
            .EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/identidad/usuarios/{usuarioId}/asignaciones",
            new { EmpresaId = EmpresaBootstrapId, RolId = rolId })).EnsureSuccessStatusCode();

        return await LoginAsync(f, oid, email);
    }
}
