using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// C7.7 (86e3jvv4q): conciliar a demanda deja la corrida registrada, la lista y el
/// detalle la devuelven, y sin permiso no se concilia.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ConciliacionCarteraEndpointsTests
{
    private const string Base = "/api/v1/cuentas-por-cobrar/conciliaciones";
    private readonly WebApplicationFactory<Program> _factory;

    public ConciliacionCarteraEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Conciliar_a_demanda_registra_la_corrida_y_se_consulta_en_lista_y_detalle()
    {
        using var client = await Login();
        // Un día sin operación en la base de pruebas: la corrida queda registrada (C7.7-b).
        var fecha = new DateOnly(2001, 1, 2);

        var post = await Conciliar(client, fecha);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var corrida = await post.Content.ReadFromJsonAsync<JsonElement>();
        var id = corrida.GetProperty("id").GetGuid();
        Assert.Equal("2001-01-02", corrida.GetProperty("fecha").GetString());
        Assert.Equal("Demanda", corrida.GetProperty("origen").GetString());
        Assert.Equal(0, corrida.GetProperty("totalDiferencias").GetInt32());

        var lista = await client.GetFromJsonAsync<JsonElement>($"{Base}?desde=2001-01-02&hasta=2001-01-02");
        Assert.Contains(lista.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        var detalle = await client.GetFromJsonAsync<JsonElement>($"{Base}/{id}");
        Assert.Equal(id, detalle.GetProperty("corrida").GetProperty("id").GetGuid());
        var reporte = detalle.GetProperty("reporte");
        Assert.Equal("Conciliación de cartera del 02/01/2001", reporte.GetProperty("titulo").GetString());
        Assert.Empty(reporte.GetProperty("filas").EnumerateArray());
    }

    [Fact]
    public async Task Conciliar_un_dia_futuro_responde_400()
    {
        using var client = await Login();

        var r = await Conciliar(client, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Sin_permiso_no_concilia_ni_consulta()
    {
        using var client = await Login("test-no-perms");

        Assert.Equal(HttpStatusCode.Forbidden, (await Conciliar(client, new DateOnly(2001, 1, 3))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Base)).StatusCode);
    }

    [Fact]
    public async Task Detalle_inexistente_responde_404()
    {
        using var client = await Login();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Base}/{Guid.NewGuid()}")).StatusCode);
    }

    private static Task<HttpResponseMessage> Conciliar(HttpClient client, DateOnly fecha)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Base) { Content = JsonContent.Create(new { fecha }) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba C7.7", EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
