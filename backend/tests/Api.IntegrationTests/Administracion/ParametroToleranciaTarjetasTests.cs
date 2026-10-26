using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Edición de la tolerancia de tarjetas (<c>cxp.tolerancia-tarjetas-mxn</c>) por el endpoint
/// existente de parámetros: listado, PATCH, bitácora de auditoría y aislamiento respecto a la
/// tolerancia factura contra OC. Cada prueba que edita restaura el valor original.
/// </summary>
public class ParametroToleranciaTarjetasTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string EndpointBase = "/api/v1/admin/parametros";
    private const string ClaveTarjetas = ToleranciaTarjetasParametro.Clave;
    private const string ClaveOc = "cxp.tolerancia-factura-contra-oc-mxn";

    private readonly WebApplicationFactory<Program> _factory;

    public ParametroToleranciaTarjetasTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Get_Modulo_Cxp_Incluye_Tolerancia_Tarjetas_Con_Valor_Por_Omision()
    {
        using var client = await CreateClientAsync("dev-superadmin");
        var item = await GetParametroAsync(client, ClaveTarjetas);
        Assert.Equal(1.00m, decimal.Parse(item.GetProperty("valor").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Patch_Actualiza_Audita_Antes_Despues_Y_No_Toca_Tolerancia_Oc()
    {
        using var client = await CreateClientAsync("dev-superadmin");
        var original = await GetParametroAsync(client, ClaveTarjetas);
        var valorOriginal = original.GetProperty("valor").GetString()!;
        var id = original.GetProperty("id").GetGuid();
        var ocAntes = (await GetParametroAsync(client, ClaveOc)).GetProperty("valor").GetString();
        var nuevo = valorOriginal == "1.50" ? "1.75" : "1.50";
        try
        {
            using var patch = await client.PatchAsJsonAsync($"{EndpointBase}/{ClaveTarjetas}", new { Valor = nuevo });
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            Assert.Equal(nuevo, (await ReadJsonAsync(patch)).GetProperty("valor").GetString());

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            var entradas = await db.AuditLog.AsNoTracking()
                .Where(a => a.Entidad == nameof(ParametroGlobal) && a.EntidadId == id && a.Operacion == "actualizar")
                .OrderByDescending(a => a.Timestamp).ToListAsync();
            var cambio = entradas.First();
            Assert.NotNull(cambio.UsuarioId);
            Assert.True(cambio.Timestamp > DateTimeOffset.UtcNow.AddMinutes(-5));
            using var diff = JsonDocument.Parse(cambio.Cambios);
            var valor = diff.RootElement.GetProperty("diff").GetProperty("Valor");
            Assert.Equal(valorOriginal, valor.GetProperty("antes").GetString());
            Assert.Equal(nuevo, valor.GetProperty("despues").GetString());

            Assert.Equal(ocAntes, (await GetParametroAsync(client, ClaveOc)).GetProperty("valor").GetString());
        }
        finally
        {
            using var restore = await client.PatchAsJsonAsync($"{EndpointBase}/{ClaveTarjetas}", new { Valor = valorOriginal });
            restore.EnsureSuccessStatusCode();
        }
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task Patch_Valor_Invalido_Retorna_422_Y_No_Modifica(string valor)
    {
        using var client = await CreateClientAsync("dev-superadmin");
        var antes = (await GetParametroAsync(client, ClaveTarjetas)).GetProperty("valor").GetString();

        using var response = await client.PatchAsJsonAsync($"{EndpointBase}/{ClaveTarjetas}", new { Valor = valor });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(antes, (await GetParametroAsync(client, ClaveTarjetas)).GetProperty("valor").GetString());
    }

    [Fact]
    public async Task Patch_Sin_Permiso_Editar_Retorna_403()
    {
        using var client = await CreateClientAsync($"par-sin-perm-{Guid.NewGuid():N}");
        using var response = await client.PatchAsJsonAsync($"{EndpointBase}/{ClaveTarjetas}", new { Valor = "2.00" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<JsonElement> GetParametroAsync(HttpClient client, string clave)
    {
        using var response = await client.GetAsync($"{EndpointBase}?modulo=cxp");
        response.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(response)).GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("clave").GetString() == clave);
    }

    private async Task<HttpClient> CreateClientAsync(string oid)
    {
        var client = _factory.CreateClientWithIdempotency();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = $"{oid}@test.local",
            Nombre = "QA Tolerancia Tarjetas",
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
