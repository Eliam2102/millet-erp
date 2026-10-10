using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Integraciones.Aw.Application.Origen;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.IntegracionesAw;

[Collection("AwClientesSync")]
public sealed class AwOrigenEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string Url = "/api/v1/integraciones/aw/origen";
    private string _original = "Real";
    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var p = await db.ParametrosGlobales.SingleAsync(x => x.Clave == AwOrigenParametro.Clave);
        _original = p.Valor;
        p.ActualizarValor("Real");
        await db.SaveChangesAsync();
    }
    public async Task DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var p = await db.ParametrosGlobales.SingleAsync(x => x.Clave == AwOrigenParametro.Clave);
        p.ActualizarValor(_original);
        await db.SaveChangesAsync();
    }
    private WebApplicationFactory<Program> Configurar(bool permitido, bool configurada = true) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> {
                ["IntegracionesAw:OrigenDemo:Permitido"] = permitido.ToString(),
                ["ConnectionStrings:AwOrigenPgDb"] = configurada ? "Host=localhost;Database=aw_demo;Username=demo" : "",
            })));
    private static async Task<HttpClient> Cliente(WebApplicationFactory<Program> app, bool admin = true)
    {
        var client = app.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new {
            EntraOid = admin ? "dev-superadmin" : "dev-aw-origen-sin-permiso",
            Email = admin ? "superadmin@dev.local" : "aw-origen@demo.invalid", Nombre = "Prueba origen A+W", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
    [Fact]
    public async Task Sin_permiso_no_puede_cambiar_pero_puede_leer_la_etiqueta()
    {
        using var app = Configurar(true);
        using var client = await Cliente(app, admin: false);
        (await client.PutAsJsonAsync(Url, new { Origen = "Demo" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
    [Theory]
    [InlineData("Demo")]
    [InlineData("Real")]
    public async Task Ambiente_apagado_rechaza_cualquier_cambio(string origen)
    {
        using var app = Configurar(false);
        using var client = await Cliente(app);
        var response = await client.PutAsJsonAsync(Url, new { Origen = origen });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be("El origen de demo no está permitido en este ambiente");
    }
    [Fact]
    public async Task Demo_sin_cadena_es_error_claro_y_conserva_real()
    {
        using var app = Configurar(true, false);
        using var client = await Cliente(app);
        var response = await client.PutAsJsonAsync(Url, new { Origen = "Demo" });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Be("La copia de demo de A+W no está configurada en este ambiente");
        (await client.GetFromJsonAsync<AwOrigenEstado>(Url))!.Origen.Should().Be("Real");
    }
    [Fact]
    public async Task Cambio_autorizado_queda_auditado_y_get_lo_refleja_y_etag_viejo_conflicta()
    {
        using var app = Configurar(true);
        using var client = await Cliente(app);
        var initial = await client.GetAsync(Url);
        using var put = new HttpRequestMessage(HttpMethod.Put, Url) { Content = JsonContent.Create(new { Origen = "Demo" }) };
        put.Headers.IfMatch.Add(initial.Headers.ETag!);
        var response = await client.SendAsync(put);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var estado = (await client.GetFromJsonAsync<AwOrigenEstado>(Url))!;
        estado.Origen.Should().Be("Demo");
        estado.CambiadoPor.Should().NotBeNullOrWhiteSpace();
        estado.CambiadoEn.Should().NotBeNull();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var audit = await db.AuditLog.AsNoTracking().Where(x => x.EntidadId == AwOrigenParametro.Id && x.UsuarioId != null)
            .OrderByDescending(x => x.Timestamp).FirstAsync();
        using var cambios = JsonDocument.Parse(audit.Cambios);
        var valor = cambios.RootElement.GetProperty("diff").GetProperty("Valor");
        valor.GetProperty("antes").GetString().Should().Be("Real");
        valor.GetProperty("despues").GetString().Should().Be("Demo");
        using var stale = new HttpRequestMessage(HttpMethod.Put, Url) { Content = JsonContent.Create(new { Origen = "Real" }) };
        stale.Headers.IfMatch.Add(initial.Headers.ETag!);
        (await client.SendAsync(stale)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
    [Fact]
    public async Task Patch_generico_no_elude_los_controles_del_origen()
    {
        using var client = await Cliente(factory);
        var r = await client.PatchAsJsonAsync("/api/v1/admin/parametros/" + AwOrigenParametro.Clave, new { Valor = "Demo" });
        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
