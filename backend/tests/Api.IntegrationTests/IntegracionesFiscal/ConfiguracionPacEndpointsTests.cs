using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.IntegracionesFiscal;

public sealed class ConfiguracionPacEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Base = "/api/v1/integraciones/fiscal/configuracion";
    private readonly WebApplicationFactory<Program> _factory;

    public ConfiguracionPacEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Get_sin_token_retorna_401()
    {
        var response = await _factory.CreateClient().GetAsync($"{Base}/{EmpresaId}/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Usuario_sin_permisos_no_puede_leer_guardar_ni_probar()
    {
        var client = await CreateClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/{EmpresaId}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Base}/{EmpresaId}/1", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Base}/{EmpresaId}/1/test", new { })).StatusCode);
    }

    [Fact]
    public async Task Lector_obtiene_configuracion_enmascarada_y_no_puede_mutar()
    {
        var admin = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalAdministrar);
        (await admin.PutAsJsonAsync($"{Base}/{EmpresaId}/1", Body())).EnsureSuccessStatusCode();
        var reader = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalLeer);

        var get = await reader.GetAsync($"{Base}/{EmpresaId}/1");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        AssertNoSecrets(await get.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"{Base}/{EmpresaId}/1", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync($"{Base}/{EmpresaId}/1/test", new { })).StatusCode);
    }

    [Fact]
    public async Task Put_sin_idempotency_key_retorna_400()
    {
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalAdministrar, autoIdempotency: false);

        var response = await client.PutAsJsonAsync($"{Base}/{EmpresaId}/1", Body());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_con_key_repetida_devuelve_misma_respuesta_sin_exponer_secretos()
    {
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalAdministrar, autoIdempotency: false);
        var key = Guid.NewGuid().ToString("D");

        var first = await PutAsync(client, key);
        var replay = await PutAsync(client, key);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        AssertNoSecrets(await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Operaciones_cross_tenant_retornan_403_y_test_no_llama_al_sdk()
    {
        var sdk = new StubSdk();
        await using var factory = FactoryWith(sdk);
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalLeer,
            PermisosCanonicos.IntegracionesFiscalAdministrar, factory: factory);
        var ajena = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/{ajena}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Base}/{ajena}/1", Body())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"{Base}/{ajena}/1/test", new { })).StatusCode);
        Assert.Equal(0, sdk.PingCount);
    }

    [Theory]
    [InlineData(true, 200, "Conexión disponible", "Conexión exitosa con FiscalAPI.")]
    [InlineData(false, 0, "timeout interno", "No fue posible conectar con FiscalAPI dentro del tiempo esperado.")]
    [InlineData(false, 422, "config interna", "Configura y activa las credenciales de FiscalAPI antes de probar la conexión.")]
    [InlineData(false, 503, "respuesta interna", "FiscalAPI no está disponible temporalmente. Intenta de nuevo.")]
    public async Task Test_con_doble_devuelve_resultado_normalizado_sin_secretos(
        bool exitosa, int status, string mensaje, string esperado)
    {
        var sdk = new StubSdk { Resultado = new(exitosa, status, mensaje, 7, DateTimeOffset.UtcNow) };
        await using var factory = FactoryWith(sdk);
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalAdministrar, factory: factory);

        var response = await client.PostAsJsonAsync($"{Base}/{EmpresaId}/1/test", new { });
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, sdk.PingCount);
        var body = JsonDocument.Parse(json).RootElement;
        Assert.Equal(exitosa, body.GetProperty("exitosa").GetBoolean());
        Assert.Equal(esperado, body.GetProperty("mensaje").GetString());
        Assert.DoesNotContain(mensaje, json, StringComparison.Ordinal);
        AssertNoSecrets(json);
    }

    [Fact]
    public async Task Test_con_configuracion_persistida_no_la_muta()
    {
        var sdk = new StubSdk();
        await using var factory = FactoryWith(sdk);
        var client = await CreateClientAsync(
            PermisosCanonicos.IntegracionesFiscalLeer,
            PermisosCanonicos.IntegracionesFiscalAdministrar,
            factory: factory);
        (await client.PutAsJsonAsync($"{Base}/{EmpresaId}/1", Body())).EnsureSuccessStatusCode();
        var before = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();

        (await client.PostAsJsonAsync($"{Base}/{EmpresaId}/1/test", new { })).EnsureSuccessStatusCode();
        var after = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();

        Assert.Equal(before, after);
    }

    private WebApplicationFactory<Program> FactoryWith(StubSdk sdk) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFiscalApiSdkClient>();
            services.AddSingleton<IFiscalApiSdkClient>(sdk);
        }));

    private async Task<HttpClient> CreateClientAsync(
        string? permiso1 = null,
        string? permiso2 = null,
        bool autoIdempotency = true,
        WebApplicationFactory<Program>? factory = null)
    {
        factory ??= _factory;
        var oid = $"adm09-{Guid.NewGuid():N}";
        if (permiso1 is not null)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var userId = Guid.CreateVersion7();
            var roleId = Guid.CreateVersion7();
            db.Roles.Add(new Rol(roleId, oid, "Rol temporal ADM-09"));
            foreach (var codigo in new[] { permiso1, permiso2 }.Where(x => x is not null))
            {
                var permissionId = await db.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), roleId, permissionId));
            }
            db.Usuarios.Add(new Usuario(userId, oid, $"{oid}@test.local", "QA ADM-09"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), userId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), userId, EmpresaId, roleId, asignadoPorUsuarioId: null));
            await db.SaveChangesAsync();
        }

        var client = autoIdempotency ? factory.CreateClientWithIdempotency() : factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid, Email = $"{oid}@test.local", Nombre = "QA ADM-09", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await ReadJsonAsync(login)).GetProperty("accessToken").GetString());
        return client;
    }

    private static object Body() => new
    {
        baseUrl = "https://test.fiscalapi.com",
        apiKey = "dummy-not-a-secret",
        activo = true,
    };

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/{EmpresaId}/1")
        {
            Content = JsonContent.Create(Body()),
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static void AssertNoSecrets(string json)
    {
        Assert.DoesNotContain("dummy-not-a-secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("certificadoBase64", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("llavePrivadaBase64", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKeyHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cifrado", json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private sealed class StubSdk : IFiscalApiSdkClient
    {
        public int PingCount { get; private set; }
        public PingResultDto Resultado { get; init; } = new(true, 200, "Conexión disponible", 7, DateTimeOffset.UtcNow);
        public Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken)
        {
            PingCount++;
            return Task.FromResult(Resultado);
        }
        public Task<PersonExterno> AsegurarPersonAsync(Guid empresaId, string rfc, string legalName, string zipCode, string satTaxRegimeCode, string? satCfdiUseCode, string email, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SincronizarPersonAsync(Guid empresaId, string personIdExterno, string legalName, string zipCode, string? satCfdiUseCode, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TaxFilesSubidos> SubirTaxFilesAsync(Guid empresaId, string personIdExterno, string rfc, byte[] cerBytes, byte[] keyBytes, string password, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(Guid empresaId, string personIdExterno, SatQueryType satQueryType, DownloadType downloadType, SatInvoiceStatusFilter satInvoiceStatus, string descripcion, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(Guid empresaId, string ruleIdExterno, DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(Guid empresaId, string requestIdExterno, CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(Guid empresaId, string requestIdExterno, CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(Guid empresaId, string requestIdExterno, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<EstatusUuidDto> ConsultarEstatusUuidAsync(Guid empresaId, string uuidCfdi, string rfcEmisor, string rfcReceptor, decimal total, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
