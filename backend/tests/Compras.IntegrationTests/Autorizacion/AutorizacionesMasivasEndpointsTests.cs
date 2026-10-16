using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// Tests de integración de S2 contra Postgres real:
/// <list type="bullet">
///   <item>la autorización masiva procesa cada id: 2 válidos + 1 inexistente ⇒ 2 ok + 1 error,
///     sin abortar el lote;</item>
///   <item>la bandeja de pendientes no expone montos (la RQ no lleva monto; se valida en la OC)
///     y sí devuelve <c>EsRecurrente</c> (3 RQ del mismo requisitante y clasificación en 60 días).</item>
/// </list>
/// </summary>
public class AutorizacionesMasivasEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";

    // Artículo sembrado (existe en el catálogo, así que la línea valida).
    private static readonly Guid ArticuloSembrado = Guid.Parse("00000005-0002-0000-0000-000000000001");

    private readonly StubsWebApplicationFactory _factory;

    public AutorizacionesMasivasEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AutorizacionMasiva_DosValidosUnInexistente_DosOkYUnError()
    {
        var client = await CreateSuperAdminClientAsync(_factory);
        var rq1 = await CrearTransmitirSinAutorizarAsync(client, new[] { (ArticuloSembrado, 2m) });
        var rq2 = await CrearTransmitirSinAutorizarAsync(client, new[] { (ArticuloSembrado, 3m) });
        var inexistente = Guid.NewGuid();

        var resp = await client.PostAsJsonAsync(
            "/api/v1/compras/requisiciones/autorizaciones-masivas",
            new { Ids = new[] { rq1, inexistente, rq2 }, Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var resultados = (await ReadJsonAsync(resp)).GetProperty("resultados").EnumerateArray().ToList();
        Assert.Equal(3, resultados.Count);

        Assert.True(Resultado(resultados, rq1).GetProperty("ok").GetBoolean());
        Assert.True(Resultado(resultados, rq2).GetProperty("ok").GetBoolean());

        var fallido = Resultado(resultados, inexistente);
        Assert.False(fallido.GetProperty("ok").GetBoolean());
        Assert.Equal(JsonValueKind.String, fallido.GetProperty("errorCodigo").ValueKind);
    }

    [Fact]
    public async Task AutorizacionMasiva_ExcepcionNoPrevista_ItemErrorInesperadoSinDetalleInterno()
    {
        // Stock que falla con una excepción no de dominio: el lote no debe abortar ni filtrar el detalle.
        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IConsultarStockPort>();
            services.AddSingleton<IConsultarStockPort>(new StockQueFalla());
        }));
        var client = await CreateSuperAdminClientAsync(factory);
        var rq = await CrearTransmitirSinAutorizarAsync(client, new[] { (ArticuloSembrado, 2m) });

        var resp = await client.PostAsJsonAsync(
            "/api/v1/compras/requisiciones/autorizaciones-masivas",
            new { Ids = new[] { rq }, Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var crudo = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("detalle-interno-stock", crudo);

        using var doc = JsonDocument.Parse(crudo);
        var fallido = Resultado(doc.RootElement.GetProperty("resultados").EnumerateArray().ToList(), rq);
        Assert.False(fallido.GetProperty("ok").GetBoolean());
        // La falla del puerto stock ocurre en la bifurcación, que la traduce a BIFURCACION_FALLO (sin detalle interno).
        Assert.Equal("BIFURCACION_FALLO", fallido.GetProperty("errorCodigo").GetString());
    }

    private sealed class StockQueFalla : IConsultarStockPort
    {
        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("detalle-interno-stock");
    }

    [Fact]
    public async Task Pendientes_NoExponeMontos_Y_DevuelveRecurrencia()
    {
        var client = await CreateSuperAdminClientAsync(_factory);
        // Mismo requisitante (superadmin) y clasificación ⇒ 3 RQ en la ventana ⇒ recurrente.
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add(await CrearTransmitirSinAutorizarAsync(client, new[] { (ArticuloSembrado, 4m) }));
        }

        var resp = await client.GetAsync("/api/v1/compras/pendientes-autorizacion?limit=200");
        resp.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(resp)).GetProperty("items").EnumerateArray().ToList();

        var item = items.Single(i => i.GetProperty("id").GetGuid() == ids[0]);
        // Desde la minuta del 12-oct la RQ no lleva monto: el item no expone montos ni menor cuantía.
        Assert.False(item.TryGetProperty("montoEstimado", out _));
        Assert.False(item.TryGetProperty("montoFaltante", out _));
        Assert.False(item.TryGetProperty("moneda", out _));
        Assert.False(item.TryGetProperty("esMenorCuantia", out _));
        Assert.True(item.GetProperty("esRecurrente").GetBoolean());
        Assert.True(item.TryGetProperty("disponibilidad", out _));
    }

    // ── Seeding y helpers (mismo patrón que PreviewCubrimientoEndpointsTests) ──

    private static JsonElement Resultado(IEnumerable<JsonElement> resultados, Guid id) =>
        resultados.Single(r => r.GetProperty("id").GetGuid() == id);

    private static async Task<HttpClient> CreateSuperAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CrearTransmitirSinAutorizarAsync(
        HttpClient client, IReadOnlyList<(Guid articuloId, decimal cantidad)> lineas)
    {
        var rqId = await CrearConLineasAsync(client, lineas);
        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();
        return rqId;
    }

    private static async Task<Guid> CrearConLineasAsync(
        HttpClient client, IReadOnlyList<(Guid articuloId, decimal cantidad)> lineas)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration S2 autorización masiva");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", rqBody);
        crear.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        foreach (var (articuloId, cantidad) in lineas)
        {
            var lineaBody = new
            {
                ArticuloId = articuloId,
                Cantidad = cantidad,
                UnidadMedida = "PZA",
                PrecioEstimadoMonto = 15m,
                PrecioEstimadoMoneda = "MXN",
                CuentaContableId = (Guid?)null,
                CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
                Proyecto = (string?)null,
                FechaRequerida = (DateOnly?)null,
                Notas = (string?)null,
            };
            var linea = await client.PostAsJsonAsync(
                $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
            linea.EnsureSuccessStatusCode();
        }

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
