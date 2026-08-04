using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Millet.Api.IntegrationTests.Fixtures;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de transmitir + autorizar v0 + listar motivos rechazo
/// (F2-PR3). Cubre el flujo Borrador → EnAutorizacion → bifurcación.
///
/// Sin configuración en <c>compras.umbrales_aprobacion_departamento</c>,
/// el evaluador v0 considera umbral = decimal.MaxValue → fail-open
/// (solo N1 cumple). Una sola autorización N1 cumple la matriz; el
/// handler de Autorizar (F4-PR1) consulta <c>IConsultarStockPort</c>
/// (stub con DefaultRatio=1.0) y la RQ termina en <c>Cerrada</c> porque
/// el stub cubre toda la cantidad solicitada.
/// </summary>
public class AutorizacionesEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public AutorizacionesEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // --- POST /api/v1/compras/requisiciones/{id}/transmitir ---

    [Fact]
    public async Task Transmitir_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/transmitir",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Transmitir_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/transmitir",
            content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Transmitir_RqInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/transmitir",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transmitir_SinLineas_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);

        // Sin agregar líneas → transmitir debería fallar.
        var response = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/transmitir",
            content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("TRANSMITIR_SIN_LINEAS", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Transmitir_ConLinea_Retorna_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearRequisicionAsync(client);
        await AgregarLineaAsync(client, requisicionId);

        var response = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/transmitir",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- POST /api/v1/compras/requisiciones/{id}/autorizaciones ---

    [Fact]
    public async Task Autorizar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // REGRESIÓN PRE-EXISTENTE: este test asume que IConsultarStockPort
    // devuelve Disponible >= Cantidad (lo que TestAssemblyInit intentaba
    // forzar via Compras__Stubs__Stock__DefaultRatio=1.0), y bajo ese
    // supuesto la RQ transiciona a Cerrada (estado=4). Pero desde
    // PR #293/#301 el stub config-driven se reemplazó por
    // AlmacenStockReadAdapter (consulta real a almacen.saldos_inventario),
    // que ignora el env var. Sin saldos sembrados para el (ArticuloSeedId,
    // AlmacenDestinoId) usado aquí, Disponible=0 → la RQ pasa a EnSurtido
    // (estado=3), no Cerrada. Test queda determinísticamente rojo —
    // verificado en main pre-PR-A2: 3/3 corridas aisladas fallan.
    //
    // Remediaciones (fuera de scope PR-A2):
    //   1. Seedear almacen.saldos_inventario con stock suficiente para
    //      ArticuloSeedId en los sub-almacenes de AlmacenMidGeneral, y
    //      actualizar el comment del test para reflejar el flujo real.
    //   2. Eliminar el test (la cobertura de "RQ → Cerrada via stock total"
    //      vive en otra suite que sí controla el saldo).
    //
    // Ver doc 01-diseño §13 Rev. 21 (hallazgo lateral B).
    [Fact]
    public async Task Autorizar_Nivel1_FailOpen_StockTotal_TransicionaA_Cerrada_Retorna_204()
    {
        // F2-PR3 esperaba transición a Autorizada (estado=2). F4-PR1
        // agregó la bifurcación stock-aware: tras autorizar, el handler
        // consulta IConsultarStockPort por línea y aplica RegistrarCubrimiento.
        // En dev (Compras:UseStubs=true en appsettings.Development.json), el
        // stub responde con DefaultRatio=1.0 → OnHand=100 cubre la línea de
        // cantidad=10 → CantidadDeCompra=0 en todas → Cerrada (estado=4).
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearYTransmitirAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/autorizaciones",
            new { Nivel = 1, Notas = "Aprobado por Super Admin" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{requisicionId}");
        get.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(get);
        Assert.Equal(4, json.GetProperty("estado").GetInt32()); // Cerrada
    }

    [Fact]
    public async Task Autorizar_Duplicado_NivelMismo_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var requisicionId = await CrearYTransmitirAsync(client);

        var first = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        first.EnsureSuccessStatusCode();

        // Tras la primera autorización (fail-open + bifurcación F4-PR1
        // con stock total → Cerrada), la RQ ya no está en EnAutorizacion.
        // El segundo POST debería fallar con AUTORIZAR_SOLO_EN_AUTORIZACION.
        var second = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    // --- GET /api/v1/compras/motivos-rechazo ---

    [Fact]
    public async Task ListarMotivos_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/compras/motivos-rechazo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListarMotivos_Con_Permiso_Retorna_6_Motivos_Seed()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/v1/compras/motivos-rechazo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);
        var motivos = json.EnumerateArray().ToList();
        Assert.Equal(6, motivos.Count);
        // RECH-OTRO debe tener permite_texto_libre = true.
        var otro = motivos.Single(m => m.GetProperty("clave").GetString() == "RECH-OTRO");
        Assert.True(otro.GetProperty("permiteTextoLibre").GetBoolean());
    }

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F2-PR3");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task AgregarLineaAsync(HttpClient client, Guid requisicionId)
    {
        var body = new
        {
            ArticuloId = ArticuloSeedId,
            Cantidad = 10m,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)Guid.Parse("0000000c-0005-0000-0000-000000000001"), // Fase E PR2.1: CC obligatorio
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/lineas",
            body);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CrearYTransmitirAsync(HttpClient client)
    {
        var requisicionId = await CrearRequisicionAsync(client);
        await AgregarLineaAsync(client, requisicionId);
        var transmitir = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{requisicionId}/transmitir",
            content: null);
        transmitir.EnsureSuccessStatusCode();
        return requisicionId;
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
