using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Nivel;

/// <summary>
/// Tests de integración del nivel pendiente en la bandeja de pendientes (PR-A)
/// contra Postgres real. Verifican el DUAL-ENCODING WHERE/Derivar:
/// <list type="bullet">
///   <item>(1) el list-query DERIVA el nivel correcto por RQ (falta N1 / falta
///     N2) — y de paso PRUEBA que los <c>EXISTS</c> por nivel traducen a SQL: si
///     no tradujeran, EF lanzaría al ejecutar;</item>
///   <item>(2) el FILTRO server-side <c>?nivelPendiente=1|2</c> devuelve
///     exactamente las RQs de ese nivel (membership + <c>Assert.All</c> del
///     valor, robusto al Postgres compartido entre tests);</item>
///   <item>(3) <c>nivelPendiente</c> inválido ⇒ 400.</item>
/// </list>
/// <para>Fuerza la matriz N1YN2 con <see cref="AlwaysN1YN2Evaluator"/> (mismo
/// patrón que <c>TwoLevelAuthEndpointsTests</c>) para poder dejar una RQ en
/// <c>EnAutorizacion</c> con N1 firmado y N2 pendiente, sin depender de
/// umbrales sembrados.</para>
/// </summary>
public class NivelPendienteEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private static readonly Guid Articulo = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public NivelPendienteEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bandeja_DerivaNivelPendiente_YFiltraPorNivel()
    {
        await using var twoLevelFactory = WithTwoLevelEvaluator();
        var client = await CreateSuperAdminClientAsync(twoLevelFactory);

        // Pendiente N1: transmitida, SIN firmas.
        var rqN1 = await CrearTransmitirAsync(client, Articulo, 10m);

        // Pendiente N2: transmitida + N1 firmado. Matriz N1YN2 ⇒ sigue EnAutorizacion.
        var rqN2 = await CrearTransmitirAsync(client, Articulo, 10m);
        await AutorizarN1Async(client, rqN2);

        // Sanity: rqN2 sigue EnAutorizacion (no saltó a Autorizada con solo N1).
        var afterN1 = await ReadJsonAsync(
            await client.GetAsync($"/api/v1/compras/requisiciones/{rqN2}"));
        Assert.Equal(
            (int)EstadoRequisicion.EnAutorizacion,
            afterN1.GetProperty("estado").GetInt32());

        // ── (1) Nivel mostrado: sin filtro, cada RQ trae su nivel pendiente ──
        var sinFiltro = await GetPendientesMapAsync(client, nivelPendiente: null);
        Assert.Equal((int)NivelAutorizacion.Nivel1, sinFiltro[rqN1]);
        Assert.Equal((int)NivelAutorizacion.Nivel2, sinFiltro[rqN2]);

        // ── (2) Filtro server-side: cada nivel devuelve solo lo suyo ──
        var soloN1 = await GetPendientesMapAsync(client, nivelPendiente: 1);
        Assert.True(soloN1.ContainsKey(rqN1));
        Assert.False(soloN1.ContainsKey(rqN2));
        // Ninguna fila filtrada por N1 puede traer otro nivel (no leak).
        Assert.All(soloN1.Values, v => Assert.Equal((int)NivelAutorizacion.Nivel1, v));

        var soloN2 = await GetPendientesMapAsync(client, nivelPendiente: 2);
        Assert.True(soloN2.ContainsKey(rqN2));
        Assert.False(soloN2.ContainsKey(rqN1));
        Assert.All(soloN2.Values, v => Assert.Equal((int)NivelAutorizacion.Nivel2, v));
    }

    [Fact]
    public async Task Bandeja_NivelPendienteInvalido_Devuelve400()
    {
        await using var twoLevelFactory = WithTwoLevelEvaluator();
        var client = await CreateSuperAdminClientAsync(twoLevelFactory);

        var resp = await client.GetAsync(
            "/api/v1/compras/pendientes-autorizacion?nivelPendiente=9");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Helpers de aserción ──

    /// <summary>
    /// Pagina TODA la bandeja de pendientes (el Postgres es compartido entre
    /// tests, y con orden FIFO mis RQs recién creadas quedan al final) y
    /// devuelve id→nivelPendiente (int) de cada item. Filtra por nivel si se
    /// pasa <paramref name="nivelPendiente"/>.
    /// </summary>
    private static async Task<IReadOnlyDictionary<Guid, int>> GetPendientesMapAsync(
        HttpClient client, int? nivelPendiente)
    {
        var mapa = new Dictionary<Guid, int>();
        const int limit = 200;
        var offset = 0;
        while (true)
        {
            var filtro = nivelPendiente is int n ? $"&nivelPendiente={n}" : string.Empty;
            var resp = await client.GetAsync(
                $"/api/v1/compras/pendientes-autorizacion?limit={limit}&offset={offset}{filtro}");
            resp.EnsureSuccessStatusCode();
            var root = await ReadJsonAsync(resp);
            var items = root.GetProperty("items");
            var count = items.GetArrayLength();
            foreach (var item in items.EnumerateArray())
            {
                var nivel = item.GetProperty("nivelPendiente");
                // En esta bandeja (EnAutorizacion) no debería ser null; si lo
                // fuera, se omite (no rompe el paginado).
                if (nivel.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                mapa[item.GetProperty("id").GetGuid()] = nivel.GetInt32();
            }

            if (count < limit)
            {
                break;
            }

            offset += limit;
        }

        return mapa;
    }

    // ── Setup matriz N1YN2 (patrón de TwoLevelAuthEndpointsTests) ──

    private WebApplicationFactory<Program> WithTwoLevelEvaluator() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRequiereNivelEvaluator>();
                services.AddScoped<IRequiereNivelEvaluator, AlwaysN1YN2Evaluator>();
            });
        });

    private sealed class AlwaysN1YN2Evaluator : IRequiereNivelEvaluator
    {
        public Task<RequiereNivel> EvaluarAsync(
            Requisicion requisicion, CancellationToken cancellationToken = default) =>
            Task.FromResult(RequiereNivel.N1YN2);
    }

    // ── Helpers de seeding ──

    private static async Task<HttpClient> CreateSuperAdminClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AutorizarN1Async(HttpClient client, Guid rqId)
    {
        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CrearTransmitirAsync(
        HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration PR-A nivel pendiente");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", rqBody);
        crear.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var lineaBody = new
        {
            ArticuloId = articuloId,
            Cantidad = cantidad,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)null,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var linea = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();
        return rqId;
    }

    private static async Task<string> FakeLoginAsync(
        HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
