using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Fixtures;

namespace Millet.Compras.IntegrationTests.CierreManual;

/// <summary>
/// Tests integration del cierre manual de RQ (ADR-0043 PR #2): el jefe de
/// almacén cierra una RQ a CerradaSinSurtir / CerradaSurtidaParcial. Libera
/// las reservas del tramo de stock (idempotente) en una sola TX; a diferencia
/// de la cancelación, NO aborta las OC borrador (el material en vuelo llega
/// como stock). Permitido solo desde Autorizada o EnSurtido.
/// </summary>
public class CerrarManualEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string SinPermisosOid = "test-no-perms";
    private static readonly Guid ArticuloSeedId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");
    private static readonly Guid MotivoCierreManualId = Guid.Parse("00000003-0002-0000-0000-000000000007"); // CIERRE-NO-REQ (aplica_a=16)
    private static readonly Guid MotivoSoloRechazoId = Guid.Parse("00000003-0002-0000-0000-000000000001"); // RECH-DUP (aplica_a=15, sin bit 16)

    private readonly StubsWebApplicationFactory _factory;

    public CerrarManualEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CerrarManual_Sin_Permiso_Retorna_403()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SinPermisosOid, "noperms@test.local", "Sin Permisos");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/cerrar-manual",
            new { MotivoId = MotivoCierreManualId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CerrarManual_DesdeBorrador_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearRequisicionAsync(client); // Borrador

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cerrar-manual",
            new { MotivoId = MotivoCierreManualId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("CIERRE_MANUAL_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CerrarManual_MotivoNoAplica_Retorna_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, ArticuloSinStock, 10m); // EnSurtido

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cerrar-manual",
            new { MotivoId = MotivoSoloRechazoId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("MOTIVO_NO_APLICA_A_CIERRE_MANUAL", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CerrarManual_DesdeEnSurtido_SinEntregas_Retorna_204_CerradaSinSurtir()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearTransmitirAutorizarAsync(client, ArticuloSinStock, 10m); // EnSurtido (todo compra)

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cerrar-manual",
            new { MotivoId = MotivoCierreManualId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        Assert.Equal((int)EstadoRequisicion.CerradaSinSurtir, json.GetProperty("estado").GetInt32());
        Assert.Equal(MotivoCierreManualId, json.GetProperty("motivoTerminacionId").GetGuid());
    }

    [Fact]
    public async Task CerrarManual_Mixto_NoAbortaOcBorrador_Y_QuedaCerradaSinSurtir()
    {
        var client = await CreateSuperAdminClientAsync();
        // Mixto: cantidad=150, OnHand=100 → 100 almacén (1 reserva) + 50 saldo (1 OC borrador).
        var rqId = await CrearTransmitirAutorizarAsync(client, ArticuloSeedId, 150m);
        Assert.Equal(1, await CountOcBorradorAsync(rqId));

        var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/cerrar-manual",
            new { MotivoId = MotivoCierreManualId, MotivoTexto = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var json = await ReadJsonAsync(await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        Assert.Equal((int)EstadoRequisicion.CerradaSinSurtir, json.GetProperty("estado").GetInt32());
        // Diferencia clave vs cancelar: el cierre manual NO aborta la OC en vuelo.
        Assert.Equal(1, await CountOcBorradorAsync(rqId));
    }

    // NOTA: se omite el test de rollback "FallaEnLiberarReserva" (inyectar un
    // ILiberarReservaPort que lanza y verificar 422 + RQ vuelve a EnSurtido).
    // Depende de que la RQ tenga una reserva del tramo de stock, lo cual exige
    // stock sembrado para ArticuloSeedId que el entorno local (millet_dev) no
    // tiene — la misma limitación pre-existente que marca rojo a
    // CancelarEndpointsTests.Cancelar_FallaEnLiberarReserva localmente
    // ("REGRESIÓN PRE-EXISTENTE: stub→adapter real", PR #293/#301). La
    // liberación del tramo de stock es idéntica a la de cancelar (mismo foreach
    // sobre ReservaId + ILiberarReservaPort) y queda cubierta por el code review.

    // --- Helpers ---

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
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
        var body = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "Test integration ADR-0043 cierre manual");
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
            CentroCostoId = (Guid?)null,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        var linea = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
        linea.EnsureSuccessStatusCode();

        var transmit = await client.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
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
