using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Catalogos;

public sealed class ImpuestosEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Base = "/api/v1/catalogos/impuestos";
    private readonly WebApplicationFactory<Program> _factory;
    public ImpuestosEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Alta_Vigencia_Duplicado_Desactivacion_Y_Consulta_Historica()
    {
        var client = await AdminAsync();
        var clave = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var desde = new DateOnly(2026, 9, 1);
        var hasta = new DateOnly(2026, 9, 30);
        var payload = new
        {
            Clave = clave, Nombre = "Impuesto ficticio QA", Tipo = "Traslado",
            Factor = "Tasa", Tasa = 0.12m, VigenteDesde = desde,
            VigenteHasta = (DateOnly?)hasta, Activo = true, Fuente = "Prueba local VILO",
        };
        var created = await client.PostAsJsonAsync(Base, payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var vigente = await client.GetAsync($"{Base}?fecha=2026-09-15");
        vigente.EnsureSuccessStatusCode();
        Assert.Contains((await ReadJsonAsync(vigente)).EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id);

        var fuera = await client.GetAsync($"{Base}?fecha=2026-10-01");
        fuera.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await ReadJsonAsync(fuera)).EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id);

        var duplicate = await client.PostAsJsonAsync(Base, new
        {
            payload.Clave, payload.Nombre, payload.Tipo, payload.Factor,
            payload.Tasa, VigenteDesde = new DateOnly(2026, 9, 20),
            VigenteHasta = (DateOnly?)new DateOnly(2026, 10, 20),
            payload.Activo, payload.Fuente,
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var patched = await client.PatchAsJsonAsync($"{Base}/{id}", new
        {
            Nombre = "Impuesto QA desactivado", VigenteHasta = (DateOnly?)hasta,
            Activo = false, Fuente = "Prueba local VILO",
        });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        var historico = await client.GetAsync($"{Base}?fecha=2026-09-15&incluirHistorico=true");
        historico.EnsureSuccessStatusCode();
        Assert.Contains((await ReadJsonAsync(historico)).EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id &&
                    !item.GetProperty("activo").GetBoolean());
    }

    [Fact]
    public async Task Usuario_Sin_Permisos_No_Consulta_Ni_Modifica()
    {
        var client = _factory.CreateClientWithIdempotency();
        var oid = $"impuestos-sin-perm-{Guid.NewGuid():N}";
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = $"{oid}@test.local", Nombre = "Sin permisos",
            EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        var token = (await ReadJsonAsync(login)).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Base, new { })).StatusCode);
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await ReadJsonAsync(login)).GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
