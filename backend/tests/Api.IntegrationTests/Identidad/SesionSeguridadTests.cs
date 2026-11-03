using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// 86e3x819w — seguridad de la sesión vista desde fuera del navegador: alguien
/// copia el token del ERP desde DevTools (o de un equipo compartido) y llama
/// la API directo con curl. La lista de permisos del frontend sólo oculta
/// botones; quien autoriza es el backend en cada endpoint. Estas pruebas fijan:
/// - el token del ERP dura 15 minutos;
/// - con un token válido pero sin permiso, leer o escribir responde 403;
/// - un token alterado (firma rota) o ausente responde 401.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public class SesionSeguridadTests(WebApplicationFactory<Program> factory)
{
    private const string SinPermisosOid = "test-no-perms";

    [Fact]
    public async Task El_token_del_ERP_dura_15_minutos()
    {
        var antes = DateTimeOffset.UtcNow;
        var (_, expira) = await FakeLoginAsync(SinPermisosOid);

        var vida = expira - antes;
        Assert.InRange(vida.TotalMinutes, 14.5, 15.5);
    }

    [Theory]
    [InlineData("/api/v1/facturacion/pedidos-facturables")]
    [InlineData("/api/v1/tesoreria/movimientos")]
    [InlineData("/api/v1/cuentas-por-cobrar/lineas-credito")]
    [InlineData("/api/v1/contabilidad/periodos/ejercicios")]
    [InlineData("/api/v1/identidad/usuarios")]
    [InlineData("/api/v1/cuentas-por-pagar/cfdis")]
    public async Task Token_copiado_sin_permiso_no_lee_datos_403(string ruta)
    {
        var client = await ClienteConTokenAsync(SinPermisosOid);

        var r = await client.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PERMISO_FALTANTE", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Token_copiado_sin_permiso_no_escribe_403()
    {
        var client = await ClienteConTokenAsync(SinPermisosOid);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/v1/facturacion/pedidos-facturables/")
        {
            Content = JsonContent.Create(new { clase = "VentaDirecta" }),
        };
        peticion.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var r = await client.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Token_alterado_responde_401()
    {
        var (token, _) = await FakeLoginAsync(SinPermisosOid);
        var partes = token.Split('.');
        // Cambia un carácter de la firma: el token ya no corresponde a la llave del API.
        var firma = partes[2];
        partes[2] = (firma[0] == 'A' ? 'B' : 'A') + firma[1..];
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', partes));

        var r = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var r = await factory.CreateClient().GetAsync("/api/v1/facturacion/pedidos-facturables");

        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // --- Helpers ---

    private async Task<HttpClient> ClienteConTokenAsync(string oid)
    {
        var (token, _) = await FakeLoginAsync(oid);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Token, DateTimeOffset Expira)> FakeLoginAsync(string oid)
    {
        var r = await factory.CreateClient().PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = $"{oid}@test.local",
            Nombre = "Prueba seguridad de sesión",
            EmpresaId = (Guid?)null,
        });
        r.EnsureSuccessStatusCode();
        var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
        return (cuerpo.GetProperty("accessToken").GetString()!, cuerpo.GetProperty("expiresAt").GetDateTimeOffset());
    }
}
