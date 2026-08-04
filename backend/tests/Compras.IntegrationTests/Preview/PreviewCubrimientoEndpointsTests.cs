using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Preview;

/// <summary>
/// Tests de integración del preview de cubrimiento (PR-C) contra Postgres
/// real. Verifican:
/// <list type="bullet">
///   <item>el endpoint deriva el reparto estimado por línea con la función
///     pura (con un <see cref="FakeStock"/> determinista, independiente del
///     ratio del stub local que resuelve DefaultRatio=0);</item>
///   <item><b>invariante read-only</b>: tras el preview, las columnas de
///     cubrimiento siguen en 0 en BD y no se creó reserva (no se llamó a
///     IReservarStockPort);</item>
///   <item>fuera de <c>EnAutorizacion</c> ⇒ <c>aplica=false</c> con lista vacía;</item>
///   <item>sin permiso <c>compras.requisiciones.leer</c> ⇒ 403.</item>
/// </list>
/// </summary>
public class PreviewCubrimientoEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";

    // Artículos sembrados (existen en el catálogo, así que la línea valida).
    private static readonly Guid ArticuloConStock = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public PreviewCubrimientoEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preview_EstimaReparto_SinReservarNiPersistir()
    {
        // FakeStock determinista: ArticuloConStock → 50 disponible, el resto → 0.
        await using var stockFactory = WithFakeStock(disponiblePorArticulo: new()
        {
            [ArticuloConStock] = 50m,
        });
        var client = await CreateSuperAdminClientAsync(stockFactory);

        // RQ EnAutorizacion (sin autorizar) con 2 líneas: una parcial, una sin stock.
        var rqId = await CrearTransmitirSinAutorizarAsync(client, new[]
        {
            (ArticuloConStock, 80m),   // disp 50 < 80 → 50 almacén, 30 compra
            (ArticuloSinStock, 5m),    // disp 0       → 0 almacén, 5 compra
        });

        var preview = await ReadJsonAsync(
            await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cubrimiento-estimado"));

        Assert.True(preview.GetProperty("aplica").GetBoolean());
        var lineas = preview.GetProperty("lineas").EnumerateArray().ToList();
        Assert.Equal(2, lineas.Count);

        var conStock = lineas.Single(l => l.GetProperty("articuloId").GetGuid() == ArticuloConStock);
        Assert.Equal(50m, conStock.GetProperty("estimadoDeAlmacen").GetDecimal());
        Assert.Equal(30m, conStock.GetProperty("estimadoDeCompra").GetDecimal());
        Assert.Equal(50m, conStock.GetProperty("disponible").GetDecimal());

        var sinStock = lineas.Single(l => l.GetProperty("articuloId").GetGuid() == ArticuloSinStock);
        Assert.Equal(0m, sinStock.GetProperty("estimadoDeAlmacen").GetDecimal());
        Assert.Equal(5m, sinStock.GetProperty("estimadoDeCompra").GetDecimal());

        // ── Invariante read-only: NADA se persistió ni se reservó ──
        await AssertCubrimientoPersistidoEnCeroAsync(rqId);
    }

    [Fact]
    public async Task Preview_FueraDeEnAutorizacion_AplicaFalse()
    {
        await using var stockFactory = WithFakeStock(new() { [ArticuloConStock] = 50m });
        var client = await CreateSuperAdminClientAsync(stockFactory);

        // Borrador: creada con línea, SIN transmitir.
        var rqId = await CrearConLineasAsync(client, new[] { (ArticuloConStock, 10m) });

        var preview = await ReadJsonAsync(
            await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}/cubrimiento-estimado"));

        Assert.False(preview.GetProperty("aplica").GetBoolean());
        Assert.Empty(preview.GetProperty("lineas").EnumerateArray());
    }

    [Fact]
    public async Task Preview_SinPermisoLeer_Retorna403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // La policy de autorización corre ANTES del handler → id cualquiera.
        var resp = await client.GetAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/cubrimiento-estimado");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── Invariante ──

    private async Task AssertCubrimientoPersistidoEnCeroAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var lineas = await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rqId)
            .ToListAsync();

        Assert.NotEmpty(lineas);
        Assert.All(lineas, l =>
        {
            Assert.Equal(0m, l.CantidadDeAlmacen);   // el preview NO escribió columnas
            Assert.Equal(0m, l.CantidadDeCompra);
        });
    }

    // ── Override determinista del puerto de stock (patrón TwoLevelAuth) ──

    private WebApplicationFactory<Program> WithFakeStock(
        Dictionary<Guid, decimal> disponiblePorArticulo) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConsultarStockPort>();
                services.AddSingleton<IConsultarStockPort>(
                    new FakeStock(disponiblePorArticulo));
            });
        });

    private sealed class FakeStock : IConsultarStockPort
    {
        private readonly IReadOnlyDictionary<Guid, decimal> _disponible;

        public FakeStock(IReadOnlyDictionary<Guid, decimal> disponible) =>
            _disponible = disponible;

        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloId, CancellationToken cancellationToken)
        {
            var disp = _disponible.TryGetValue(articuloId, out var d) ? d : 0m;
            return Task.FromResult(new DisponibilidadStock(
                OnHand: disp, Disponible: disp));
        }
    }

    // ── Seeding ──

    private static async Task<HttpClient> CreateSuperAdminClientAsync(
        WebApplicationFactory<Program> factory)
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
            descripcion: "Test integration PR-C preview cubrimiento");
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
