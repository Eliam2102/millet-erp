using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// C7.2 (86e3kdxy6): las series reales de Millet llegan por migración, la
/// liberación las respeta (CA7.4, CA7.5) y un cambio de regla queda en la
/// bitácora central y aplica a la siguiente evaluación (C7.2-a).
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class ReglasSerieMilletTests
{
    private const string Liberaciones = "/api/v1/cuentas-por-cobrar/liberaciones";
    private const string Admin = Liberaciones + "/reglas-serie/admin";
    private readonly WebApplicationFactory<Program> _factory;

    public ReglasSerieMilletTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Migracion_carga_las_series_de_Millet_sin_las_de_ejemplo()
    {
        using var client = await Login();

        var reglas = await client.GetFromJsonAsync<JsonElement>(Admin);
        var porSerie = reglas.EnumerateArray().ToDictionary(
            r => r.GetProperty("prefijo").GetString()!,
            r => (Comportamiento: r.GetProperty("comportamiento").GetInt32(), Activo: r.GetProperty("activo").GetBoolean()));

        foreach (var serie in new[] { "5000", "5500", "5600", "7000", "7200", "7500" })
            Assert.Equal((1, true), porSerie[serie]);
        foreach (var serie in new[] { "5800", "5900", "7800", "7900", "9000" })
            Assert.Equal((2, true), porSerie[serie]);
        Assert.DoesNotContain("3000", porSerie.Keys);
        Assert.DoesNotContain("4000", porSerie.Keys);
        Assert.DoesNotContain("8000", porSerie.Keys);
    }

    [Fact]
    public async Task CA7_4_y_CA7_5_la_liberacion_respeta_las_series_de_Millet()
    {
        using var client = await Login();
        var clienteSinCredito = Guid.NewGuid();

        var serie5500 = await Decidir(client, $"5500{Sufijo()}", clienteSinCredito);
        Assert.Equal(HttpStatusCode.Created, serie5500.StatusCode);
        var liberado = await serie5500.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, liberado.GetProperty("resultado").GetInt32());      // Liberado
        Assert.Equal(1, liberado.GetProperty("reglaAplicada").GetInt32());  // Serie

        var serie7800 = await Decidir(client, $"7800{Sufijo()}", clienteSinCredito);
        Assert.Equal(HttpStatusCode.Created, serie7800.StatusCode);
        var retenido = await serie7800.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, retenido.GetProperty("resultado").GetInt32());      // Retenido
        Assert.Equal(1, retenido.GetProperty("reglaAplicada").GetInt32());  // Serie
    }

    [Fact]
    public async Task C7_2_a_cambio_de_regla_queda_auditado_y_aplica_a_la_siguiente_evaluacion()
    {
        using var client = await Login();
        var regla = await Regla(client, "7500");
        var id = regla.GetProperty("id").GetGuid();

        var cambio = await CambiarComportamiento(client, id, regla.GetProperty("version").GetInt32(), comportamiento: 3);
        try
        {
            Assert.Equal(HttpStatusCode.OK, cambio.StatusCode);
            var cambiada = await cambio.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(3, cambiada.GetProperty("comportamiento").GetInt32());

            // Siguiente evaluación: sin línea de crédito, ya no libera por serie.
            var decision = await Decidir(client, $"7500{Sufijo()}", Guid.NewGuid());
            var cuerpo = await decision.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, cuerpo.GetProperty("resultado").GetInt32());      // Retenido
            Assert.Equal(2, cuerpo.GetProperty("reglaAplicada").GetInt32());  // Crédito

            var historial = await client.GetFromJsonAsync<JsonElement>($"{Admin}/historial?reglaId={id}");
            var ultimo = historial.EnumerateArray().First();
            Assert.Equal("7500", ultimo.GetProperty("prefijo").GetString());
            Assert.Equal("actualizar", ultimo.GetProperty("operacion").GetString());
            Assert.Equal(1, ultimo.GetProperty("comportamientoAntes").GetInt32());
            Assert.Equal(3, ultimo.GetProperty("comportamientoDespues").GetInt32());
            Assert.NotEqual("Sistema", ultimo.GetProperty("actorNombre").GetString());
        }
        finally
        {
            // El catálogo es compartido por la colección: se deja como lo cargó la migración.
            var actual = await Regla(client, "7500");
            if (actual.GetProperty("comportamiento").GetInt32() != 1)
                (await CambiarComportamiento(client, id, actual.GetProperty("version").GetInt32(), comportamiento: 1))
                    .EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Alta_de_serie_existente_es_409_y_version_vieja_es_409()
    {
        using var client = await Login();

        var duplicada = await Enviar(client, HttpMethod.Post, Admin, new { prefijo = "9000", comportamiento = 1 });
        Assert.Equal(HttpStatusCode.Conflict, duplicada.StatusCode);
        Assert.Contains("RLS_PREFIJO_DUPLICADO", await duplicada.Content.ReadAsStringAsync());

        var regla = await Regla(client, "9000");
        var vieja = await CambiarComportamiento(client, regla.GetProperty("id").GetGuid(),
            regla.GetProperty("version").GetInt32() - 1, comportamiento: 3);
        Assert.Equal(HttpStatusCode.Conflict, vieja.StatusCode);
    }

    private static string Sufijo() => Random.Shared.Next(100_000, 999_999).ToString();

    private static async Task<JsonElement> Regla(HttpClient client, string prefijo)
    {
        var reglas = await client.GetFromJsonAsync<JsonElement>(Admin);
        return reglas.EnumerateArray().Single(r => r.GetProperty("prefijo").GetString() == prefijo);
    }

    private static Task<HttpResponseMessage> CambiarComportamiento(HttpClient client, Guid id, int version, int comportamiento)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"{Admin}/{id}/comportamiento")
        {
            Content = JsonContent.Create(new { comportamiento }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Headers.Add("X-Expected-Version", version.ToString());
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Decidir(HttpClient client, string pedidoRef, Guid clienteId) =>
        Enviar(client, HttpMethod.Post, Liberaciones,
            new { pedidoRef, clienteId, moneda = "MXN", montoPedido = 1_000m, overrideId = (Guid?)null });

    private static Task<HttpResponseMessage> Enviar(HttpClient client, HttpMethod metodo, string url, object body)
    {
        var request = new HttpRequestMessage(metodo, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba C7.2", EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
