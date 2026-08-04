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
using Millet.Compras.Infrastructure;

using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.Bifurcacion;

/// <summary>
/// Tests integration de cancelar (F4-PR3): la cancelación aborta OC borrador
/// en una sola TX. Estado final <c>Cancelada</c>. Permitido solo desde
/// <c>Autorizada</c> o <c>EnSurtido</c>. (PR4: ya no hay reservas que liberar.)
/// </summary>
public class CancelarEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");
    private static readonly Guid MotivoSinTextoLibreId = Guid.Parse("00000003-0002-0000-0000-000000000001"); // RECH-DUP
    private static readonly Guid MotivoConTextoLibreId = Guid.Parse("00000003-0002-0000-0000-000000000006"); // RECH-OTRO

    private readonly StubsWebApplicationFactory _factory;

    public CancelarEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cancelar_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cancelar_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClient();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cancelar_DesdeBorrador_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        // RQ recién creada → estado Borrador.
        var rqId = await CrearRequisicionAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("CANCELAR_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO", body.GetProperty("code").GetString());
    }

    // REGRESIÓN PRE-EXISTENTE: stub→adapter real (PR #293/#301).
    // Ver doc 01 §13 Rev. 21 hallazgo lateral C (patrón sistémico en
    // Bifurcación; la causa raíz técnica está documentada en B).
    [Fact]
    public async Task Cancelar_DesdeCerradaSinSurtir_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        // ADR-0043: aun con stock total, autorizar deja la RQ EnSurtido;
        // el cierre ocurre por entrega o explícitamente. Cerramos de forma
        // manual para construir un estado terminal real y validar que desde
        // ahí la cancelación sigue rechazada.
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSeedId, cantidad: 10m);

        var cerrar = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cerrar-manual",
            new
            {
                MotivoId = Guid.Parse("00000003-0002-0000-0000-000000000007"),
                MotivoTexto = (string?)null,
            });
        cerrar.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // REGRESIÓN PRE-EXISTENTE: stub→adapter real (PR #293/#301).
    // Ver doc 01 §13 Rev. 21 hallazgo lateral C (patrón sistémico en
    // Bifurcación; la causa raíz técnica está documentada en B).
    [Fact]
    public async Task Cancelar_DesdeEnSurtido_Retorna_204_Y_BorraOcBorrador()
    {
        var client = await CreateSuperAdminClientAsync();
        // Mixto: cantidad=150, OnHand=100 → 100 almacén + 50 saldo (1 OC borrador).
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSeedId, cantidad: 150m);

        // Pre-condición: en EnSurtido, hay 1 OC borrador.
        var preGet = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        var preJson = await ReadJsonAsync(preGet);
        Assert.Equal((int)EstadoRequisicion.EnSurtido, preJson.GetProperty("estado").GetInt32());
        Assert.Equal(1, await CountOcBorradorAsync(rqId));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Estado final = Cancelada + 0 OC borrador (delete dentro de la TX).
        var get = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        var json = await ReadJsonAsync(get);
        Assert.Equal((int)EstadoRequisicion.Cancelada, json.GetProperty("estado").GetInt32());
        Assert.Equal(MotivoSinTextoLibreId, json.GetProperty("motivoTerminacionId").GetGuid());
        Assert.Equal(0, await CountOcBorradorAsync(rqId));
    }

    [Fact]
    public async Task Cancelar_Doble_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var first = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoSinTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task Cancelar_MotivoOtro_SinTexto_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = MotivoConTextoLibreId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("MOTIVO_TEXTO_REQUERIDO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Cancelar_MotivoInexistente_Retorna_404()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, articuloId: ArticuloSinStock, cantidad: 10m);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cancelar",
            new { MotivoId = Guid.NewGuid(), MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
        return await db.OcBorradorStubs.AsNoTracking().CountAsync(s => s.OrigenRequisicionId == rqId);
    }

    private static async Task<Guid> CrearRequisicionAsync(HttpClient client)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(
            descripcion: "Test integration F4-PR3");
        var response = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        response.EnsureSuccessStatusCode();
        var json = await ReadJsonAsync(response);
        return json.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CrearTransmitirAutorizarAsync(HttpClient client, Guid articuloId, decimal cantidad)
    {
        var rqId = await CrearRequisicionAsync(client);
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

        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();

        return rqId;
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
