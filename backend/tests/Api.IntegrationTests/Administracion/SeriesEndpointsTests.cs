using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del CRUD de Series + reserva de folios
/// (F-Admin-PR6.1).
///
/// <para>
/// Cubre los happy paths del CRUD + caminos negativos clave (409
/// duplicado, 422 SERIE_NO_CONFIGURADA, 200 reserva con folio
/// formateado) + concurrencia.
/// </para>
///
/// <para>
/// Cada test genera un sufijo aleatorio en el prefijo para no chocar
/// con seeds ni con datos dejados por tests previos en la DB compartida.
/// El <see cref="EmpresaBootstrapId"/> es la empresa creada por el
/// BootstrapSuperAdminHostedService (al iniciar la app de tests con
/// <c>Auth:Bootstrap:EmpresaInicial</c> configurada).
/// </para>
/// </summary>
public class SeriesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/series";

    // Empresa inicial determinista del BootstrapSuperAdminHostedService.
    private static readonly Guid EmpresaBootstrapId =
        Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public SeriesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- LIST ---

    [Fact]
    public async Task Listar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Listar_Con_SuperAdmin_Retorna_200()
    {
        var client = await CreateSuperAdminClientAsync();
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.GetProperty("total").GetInt32() >= 0);
    }

    // --- CREATE ---

    [Fact]
    public async Task Crear_Con_Datos_Validos_Retorna_201_Con_Id()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();

        var response = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4, // Poliza — evitar conflicto con OC en otros tests
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0, // None
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.NotEqual(Guid.Empty, body.GetProperty("id").GetGuid());
        Assert.Equal(prefijo, body.GetProperty("prefijo").GetString());
        Assert.True(body.GetProperty("activa").GetBoolean());
    }

    [Fact]
    public async Task Crear_Duplicado_Retorna_409()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var payload = new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4, // Poliza
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        };

        var primero = await client.PostAsJsonAsync(EndpointBase, payload);
        primero.EnsureSuccessStatusCode();

        var duplicado = await client.PostAsJsonAsync(EndpointBase, payload);
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
    }

    // --- DETALLE ---

    [Fact]
    public async Task Obtener_Detalle_Incluye_Preview_Folio()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();

        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1, // Anual
        });
        createResp.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(createResp)).GetProperty("id").GetGuid();

        var detalle = await client.GetAsync($"{EndpointBase}/{id}");
        Assert.Equal(HttpStatusCode.OK, detalle.StatusCode);
        var body = await ReadJsonAsync(detalle);

        Assert.True(body.TryGetProperty("serie", out _));
        Assert.True(body.TryGetProperty("proximoFolioPreview", out var preview));
        var previewStr = preview.GetString();
        Assert.NotNull(previewStr);
        Assert.Matches($@"^{prefijo}-\d{{4}}-000001$", previewStr);
    }

    // --- PATCH ---

    [Fact]
    public async Task Patch_Cambia_Prefijo_Y_Get_Posterior_Lo_Refleja()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var nuevoPrefijo = RandomPrefijo();

        var created = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var patched = await client.PatchAsJsonAsync($"{EndpointBase}/{id}", new
        {
            Prefijo = nuevoPrefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = (int?)null,
            LimpiarSufijo = false,
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        var verify = await client.GetAsync($"{EndpointBase}/{id}");
        verify.EnsureSuccessStatusCode();
        var verifyBody = await ReadJsonAsync(verify);
        Assert.Equal(nuevoPrefijo,
            verifyBody.GetProperty("serie").GetProperty("prefijo").GetString());
    }

    // --- DESACTIVAR ---

    [Fact]
    public async Task Desactivar_Cambia_Activa_A_False()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();

        var created = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)null,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 0,
        });
        created.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var resp = await client.PostAsync($"{EndpointBase}/{id}/desactivar", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        Assert.False(body.GetProperty("activa").GetBoolean());
    }

    // --- RESERVAR ---

    [Fact]
    public async Task Reservar_Sin_Serie_Configurada_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        // EmpresaId aleatoria — garantizado a no tener serie.
        var fakeEmpresaId = Guid.NewGuid();

        var resp = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = fakeEmpresaId,
            SucursalId = (Guid?)null,
            TipoDocumento = 1, // OrdenCompra
            FechaReferencia = "2026-05-14",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        Assert.Equal("SERIE_NO_CONFIGURADA", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reservar_Con_Serie_Activa_Retorna_200_Con_Folio_Formateado()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        // SucursalId única por test: el handler de Reservar busca primero
        // serie por (Empresa, Sucursal específica, TipoDoc); si no
        // encuentra, cae al match cross-sucursal. Usando una SucursalId
        // sintética por test evitamos chocar con series creadas por otros
        // tests en la misma DB.
        var sucursalUnica = Guid.NewGuid();

        // Crear serie Anual para reservar contra ella.
        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4, // Poliza
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1, // Anual
        });
        createResp.EnsureSuccessStatusCode();

        var resp = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
        {
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4,
            FechaReferencia = "2026-05-14",
        });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await ReadJsonAsync(resp);
        var folio = body.GetProperty("folio").GetString();
        Assert.NotNull(folio);
        Assert.Matches($@"^{prefijo}-2026-\d{{6}}$", folio);
        Assert.True(body.GetProperty("numero").GetInt64() >= 1);
    }

    [Fact]
    public async Task Reservar_10_Veces_Devuelve_10_Folios_Consecutivos()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var sucursalUnica = Guid.NewGuid();

        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1, // Anual — todos los folios mismo período "2026"
        });
        createResp.EnsureSuccessStatusCode();

        var numeros = new List<long>();
        for (int i = 0; i < 10; i++)
        {
            var r = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            {
                EmpresaId = EmpresaBootstrapId,
                SucursalId = (Guid?)sucursalUnica,
                TipoDocumento = 4,
                FechaReferencia = "2026-05-14",
            });
            r.EnsureSuccessStatusCode();
            var b = await ReadJsonAsync(r);
            numeros.Add(b.GetProperty("numero").GetInt64());
        }

        // Los números son consecutivos a partir del mínimo (otro test
        // previo puede haber reservado para ESTE prefijo solo si fueron
        // reservas del mismo prefijo — pero el prefijo es aleatorio por
        // test, así que arrancan desde 1).
        var ordenados = numeros.OrderBy(n => n).ToList();
        for (int i = 0; i < ordenados.Count - 1; i++)
        {
            Assert.Equal(ordenados[i] + 1, ordenados[i + 1]);
        }
    }

    [Fact]
    public async Task Reservar_50_Concurrentes_Devuelve_50_Folios_Unicos()
    {
        // Concurrencia: dispara 50 POSTs en paralelo con keys distintos.
        // Cada reserva debe obtener un número único — la atomicidad la
        // garantiza el ON CONFLICT DO UPDATE de la UPSERT en
        // ReservarFolioHandler.
        var client = await CreateSuperAdminClientAsync();
        var prefijo = RandomPrefijo();
        var sucursalUnica = Guid.NewGuid();

        var createResp = await client.PostAsJsonAsync(EndpointBase, new
        {
            Id = Guid.Empty,
            EmpresaId = EmpresaBootstrapId,
            SucursalId = (Guid?)sucursalUnica,
            TipoDocumento = 4,
            Prefijo = prefijo,
            Sufijo = (string?)null,
            ReinicioPeriodo = 1,
        });
        createResp.EnsureSuccessStatusCode();

        const int N = 50;
        var tasks = Enumerable.Range(0, N).Select(async _ =>
        {
            var r = await client.PostAsJsonAsync($"{EndpointBase}/reservar", new
            {
                EmpresaId = EmpresaBootstrapId,
                SucursalId = (Guid?)sucursalUnica,
                TipoDocumento = 4,
                FechaReferencia = "2026-05-14",
            });
            r.EnsureSuccessStatusCode();
            var b = await ReadJsonAsync(r);
            return b.GetProperty("numero").GetInt64();
        }).ToArray();

        var numeros = await Task.WhenAll(tasks);
        Assert.Equal(N, numeros.Length);
        Assert.Equal(N, numeros.Distinct().Count());
    }

    // --- Helpers ---

    private static string RandomPrefijo()
    {
        // Máx 10: "T" + 9 hex. Con 3 hex la BD de dev ya chocaba (409).
        return "T" + Guid.NewGuid().ToString("N").Substring(0, 9).ToUpperInvariant();
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> FakeLoginAsync(HttpClient client, string oid, string email, string nombre)
    {
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
