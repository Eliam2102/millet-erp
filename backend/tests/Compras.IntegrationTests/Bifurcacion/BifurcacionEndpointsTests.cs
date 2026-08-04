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
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.OrdenCompra;
using Millet.Compras.Infrastructure;

using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Bifurcacion;

/// <summary>
/// Tests integration de la bifurcación stock-aware (F4-PR1 + F4-PR2):
/// cuando una requisición se autoriza y la matriz queda cumplida, el
/// handler consulta <c>IConsultarStockPort</c> por línea, aplica
/// <c>RegistrarCubrimiento</c> y luego invoca <c>IGenerarSolicitudCompraPort</c>
/// con el saldo en la misma transacción EF (PR4: las RQ ya no reservan stock).
///
/// <para>
/// Estado final:
/// </para>
/// <list type="bullet">
///   <item>Stock total cubre todo → <c>Cerrada</c>, OC borrador NO creada.</item>
///   <item>Stock cero → <c>EnSurtido</c>, OC borrador creada con saldo total.</item>
///   <item>Mixto → <c>EnSurtido</c>, OC borrador creada con saldo parcial.</item>
///   <item>Falla de cualquier puerto → 422 (BIFURCACION_FALLO), TX rollback,
///         RQ regresa a <c>EnAutorizacion</c>, sin cubrimiento, sin OC.</item>
/// </list>
///
/// <para>
/// Usa <see cref="StubsWebApplicationFactory"/> que activa
/// <c>Compras:UseStubs=true</c> con:
/// </para>
/// <list type="bullet">
///   <item><c>DefaultRatio = 1.0</c> → OnHand = 100 (escala fija del stub) cubre cantidades ≤ 100.</item>
///   <item>Override <c>...bbb</c> → ratio 0.0 → OnHand = 0 → fuerza bifurcación a OC.</item>
/// </list>
/// </summary>
public class BifurcacionEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    // F7-PR1: ID del seed test data (sin override en stub → DefaultRatio=1.0 → stock total).
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    private readonly StubsWebApplicationFactory _factory;

    public BifurcacionEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // REGRESIÓN PRE-EXISTENTE: stub→adapter real (PR #293/#301).
    // Ver doc 01 §13 Rev. 21 hallazgo lateral C (patrón sistémico en
    // Bifurcación; la causa raíz técnica está documentada en B).
    [Fact]
    public async Task Autorizar_StockTotalCubreTodo_TransicionaA_EnSurtido_SinOcBorrador()
    {
        var client = await CreateSuperAdminClientAsync();
        // Articulo aleatorio (no coincide con override) → DefaultRatio=1.0,
        // OnHand=100, cantidad=10 → todo cubierto por almacén.
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSeedId, cantidad: 10m);

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        get.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(get);
        // ADR-0043 #3: el cubrimiento 100% stock YA NO cierra → EnSurtido.
        Assert.Equal((int)EstadoRequisicion.EnSurtido, json.GetProperty("estado").GetInt32());

        // Cubrimiento numerico via DbContext.
        var (alm, comp, pendiente) = await GetCubrimientoAsync(rqId);
        Assert.Equal(10m, alm);
        Assert.Equal(0m, comp);
        Assert.Equal(0m, pendiente);

        // F4-PR2: sin saldo → OC borrador NO se crea.
        Assert.Equal(0, await CountOcBorradorAsync(rqId));
    }

    [Fact]
    public async Task Autorizar_StockCero_TransicionaA_EnSurtido_ConOcBorrador()
    {
        var client = await CreateSuperAdminClientAsync();
        // Articulo con override 0.0 → OnHand=0 → todo va a OC.
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        var json = await ReadJsonAsync(get);
        Assert.Equal((int)EstadoRequisicion.EnSurtido, json.GetProperty("estado").GetInt32());

        var (alm, comp, _) = await GetCubrimientoAsync(rqId);
        Assert.Equal(0m, alm);
        Assert.Equal(10m, comp);

        // F4-PR2: hay saldo → OC borrador creada con la línea de saldo.
        Assert.Equal(1, await CountOcBorradorAsync(rqId));
    }

    // REGRESIÓN PRE-EXISTENTE: stub→adapter real (PR #293/#301).
    // Ver doc 01 §13 Rev. 21 hallazgo lateral C (patrón sistémico en
    // Bifurcación; la causa raíz técnica está documentada en B).
    [Fact]
    public async Task Autorizar_StockMixto_TransicionaA_EnSurtido_ConSaldoYOcBorrador()
    {
        var client = await CreateSuperAdminClientAsync();
        // Cantidad 150 con OnHand=100 (DefaultRatio=1.0): 100 almacén,
        // 50 saldo → mixto → EnSurtido.
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSeedId, cantidad: 150m);

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        var json = await ReadJsonAsync(get);
        Assert.Equal((int)EstadoRequisicion.EnSurtido, json.GetProperty("estado").GetInt32());

        var (alm, comp, _) = await GetCubrimientoAsync(rqId);
        Assert.Equal(100m, alm);
        Assert.Equal(50m, comp);

        // F4-PR2: parte del cubrimiento viene de OC → 1 OC borrador.
        Assert.Equal(1, await CountOcBorradorAsync(rqId));
    }

    // --- F4-PR2: fallas en la transacción cross-port ---

    [Fact]
    public async Task Autorizar_FallaEnOC_RollbackTotal_Y_RqQuedaEnAutorizacion()
    {
        await using var failFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGenerarSolicitudCompraPort>();
                services.AddScoped<IGenerarSolicitudCompraPort, ThrowingGenerarSolicitudCompraPort>();
            });
        });
        var client = await CreateSuperAdminClientAsync(failFactory);
        // Articulo con override 0.0 → todo a OC → OC port se invoca → throws.
        var rqId = await CrearTransmitirAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, auth.StatusCode);

        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        var json = await ReadJsonAsync(get);
        Assert.Equal((int)EstadoRequisicion.EnAutorizacion, json.GetProperty("estado").GetInt32());

        var (alm, comp, _) = await GetCubrimientoAsync(rqId);
        Assert.Equal(0m, alm);
        Assert.Equal(0m, comp);
    }

    // --- Test doubles que fuerzan fallo en los puertos ---

    private sealed class ThrowingGenerarSolicitudCompraPort : IGenerarSolicitudCompraPort
    {
        public Task<Guid> GenerarBorradorAsync(
            Guid origenRequisicionId, IReadOnlyList<LineaSaldo> saldoNoCubierto,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("OC submódulo caído (simulado en test).");
    }

    // --- Helpers ---

    private Task<HttpClient> CreateSuperAdminClientAsync() => CreateSuperAdminClientAsync(_factory);

    private static async Task<HttpClient> CreateSuperAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<int> CountOcBorradorAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        return await db.OcBorradorStubs
            .AsNoTracking()
            .CountAsync(s => s.OrigenRequisicionId == rqId);
    }

    private static async Task<Guid> CrearTransmitirAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F4-PR1");
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
            CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
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

    /// <summary>
    /// Lee la línea de la RQ desde la BD vía DbContext. Usa Bypass para
    /// saltar el filtro de empresa (no hay request, no hay JWT en este
    /// scope).
    /// </summary>
    private async Task<(decimal Almacen, decimal Compra, decimal Pendiente)> GetCubrimientoAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<Millet.SharedKernel.Application.ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var lineas = await db.LineaRequisiciones
            .AsNoTracking()
            .Where(l => l.RequisicionId == rqId)
            .ToListAsync();
        Assert.Single(lineas);
        var linea = lineas[0];
        return (linea.CantidadDeAlmacen, linea.CantidadDeCompra, linea.Cubrimiento.CantidadPendiente);
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
