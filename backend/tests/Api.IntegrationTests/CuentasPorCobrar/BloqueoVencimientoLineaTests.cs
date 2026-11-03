using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// C7.5 (86e3kdztq): el desbloqueo manual de una línea de crédito exige
/// motivo y deja registrado quién lo autorizó y cuándo; la migración agrega
/// las columnas del bloqueo automático.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class BloqueoVencimientoLineaTests
{
    private const string Lineas = "/api/v1/cuentas-por-cobrar/lineas-credito";
    private readonly WebApplicationFactory<Program> _factory;

    public BloqueoVencimientoLineaTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Desbloqueo_manual_exige_motivo_y_lo_registra()
    {
        using var client = await Login();

        var alta = await Enviar(client, HttpMethod.Post, Lineas, new
        {
            clienteId = Guid.NewGuid(), moneda = "MXN", limite = 100_000m, origen = 2, plazoDias = 15,
        });
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var linea = await alta.Content.ReadFromJsonAsync<JsonElement>();
        var id = linea.GetProperty("id").GetGuid();
        Assert.False(linea.GetProperty("bloqueoAutomatico").GetBoolean());

        var bloqueo = await Enviar(client, HttpMethod.Post, $"{Lineas}/{id}/bloquear",
            new { motivo = "Cartera vencida" }, linea.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.OK, bloqueo.StatusCode);
        var bloqueada = await bloqueo.Content.ReadFromJsonAsync<JsonElement>();
        // Un bloqueo manual no es automático: no se levanta solo al pagar.
        Assert.False(bloqueada.GetProperty("bloqueoAutomatico").GetBoolean());
        var version = bloqueada.GetProperty("version").GetInt32();

        var sinMotivo = await Enviar(client, HttpMethod.Post, $"{Lineas}/{id}/desbloquear",
            new { motivo = "" }, version);
        Assert.Equal(HttpStatusCode.BadRequest, sinMotivo.StatusCode);

        var desbloqueo = await Enviar(client, HttpMethod.Post, $"{Lineas}/{id}/desbloquear",
            new { motivo = "Acuerdo de pago autorizado" }, version);
        Assert.Equal(HttpStatusCode.OK, desbloqueo.StatusCode);
        var activa = await desbloqueo.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, activa.GetProperty("estado").GetInt32());
        Assert.Equal("Acuerdo de pago autorizado", activa.GetProperty("motivoDesbloqueo").GetString());
        Assert.NotEqual(JsonValueKind.Null, activa.GetProperty("desbloqueoManualEn").ValueKind);

        // Persistido: el detalle lo devuelve igual.
        var detalle = await client.GetFromJsonAsync<JsonElement>($"{Lineas}/{id}");
        Assert.Equal("Acuerdo de pago autorizado", detalle.GetProperty("motivoDesbloqueo").GetString());
    }

    private static Task<HttpResponseMessage> Enviar(
        HttpClient client, HttpMethod metodo, string url, object body, int? version = null)
    {
        var request = new HttpRequestMessage(metodo, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        if (version is int v) request.Headers.Add("X-Expected-Version", v.ToString());
        return client.SendAsync(request);
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba C7.5", EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
