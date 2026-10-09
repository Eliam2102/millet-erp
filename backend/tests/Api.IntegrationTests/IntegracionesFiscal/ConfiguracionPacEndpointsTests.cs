using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.IntegracionesFiscal;

public sealed class ConfiguracionPacEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly Guid EmpresaId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Base = "/api/v1/integraciones/fiscal/configuracion";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HashSet<string> _usuariosTemporales = [];
    private readonly HashSet<Guid> _rolesTemporales = [];

    public ConfiguracionPacEndpointsTests(WebApplicationFactory<Program> factory)
        => _factory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPacCandidatoProbe>();
            services.AddSingleton<IPacCandidatoProbe>(new StubSdk());
        }));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try
        {
            if (_usuariosTemporales.Count == 0) return;
            using var scope = _factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            var oids = _usuariosTemporales.ToArray();
            var roles = _rolesTemporales.ToArray();
            var usuarios = await db.Usuarios.Where(u => oids.Contains(u.EntraOid)).Select(u => u.Id).ToArrayAsync();
            await db.UsuarioEmpresaRoles.Where(x => usuarios.Contains(x.UsuarioId)).ExecuteDeleteAsync();
            await db.UsuarioPreferencias.Where(x => usuarios.Contains(x.UsuarioId)).ExecuteDeleteAsync();
            await db.Usuarios.Where(x => usuarios.Contains(x.Id)).ExecuteDeleteAsync();
            await db.RolPermisos.Where(x => roles.Contains(x.RolId)).ExecuteDeleteAsync();
            await db.Roles.Where(x => roles.Contains(x.Id)).ExecuteDeleteAsync();
        }
        finally
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Get_sin_token_retorna_401()
    {
        var response = await _factory.CreateClient().GetAsync($"{Base}/{EmpresaId}/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_con_fiel_rechaza_sin_rotar_configuracion_existente()
    {
        // Usuario seed: no se crean roles ni entradas de catálogos compartidos.
        var client = _factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = "dev-superadmin", Email = "dev-superadmin@dev.local",
            Nombre = "SuperAdmin", EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await ReadJsonAsync(login)).GetProperty("accessToken").GetString());
        var antes = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=FIX FIEL sin OU", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.Create(request.SubjectName,
            X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(16));
        var response = await PutCurrentAsync(client, new
        {
            baseUrl = "https://test.fiscalapi.com", apiKey = "dummy-not-a-secret", activo = true,
            csd = new
            {
                certificadoBase64 = Convert.ToBase64String(cert.Export(X509ContentType.Cert)),
                llavePrivadaBase64 = Convert.ToBase64String(rsa.ExportEncryptedPkcs8PrivateKey("prueba",
                    new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10000))),
                password = "prueba",
            },
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await ReadJsonAsync(response);
        Assert.Equal("CONFIG_PAC_CSD_ES_FIEL", error.GetProperty("code").GetString());
        Assert.Contains("Este archivo es una e.firma (FIEL), no un sello digital (CSD)", error.GetProperty("detail").GetString());
        var despues = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();
        Assert.Equal(antes, despues);
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
        (await PutCurrentAsync(admin)).EnsureSuccessStatusCode();
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
        Assert.True(JsonElement.DeepEquals(await ReadJsonAsync(first), await ReadJsonAsync(replay)));
        AssertNoSecrets(await replay.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("http://test.fiscalapi.com")]
    [InlineData("https://localhost")]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://test.fiscalapi.com.evil.example")]
    [InlineData("https://test.fiscalapi.com/api")]
    public async Task Put_rechaza_base_url_no_oficial_sin_persistir(string baseUrl)
    {
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalAdministrar);

        var response = await PutCurrentAsync(client, Body(baseUrl));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("CONFIG_PAC_BASE_URL_INVALIDA",
            (await ReadJsonAsync(response)).GetProperty("code").GetString());
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
        (await PutCurrentAsync(client, factory: factory)).EnsureSuccessStatusCode();
        var before = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();

        (await client.PostAsJsonAsync($"{Base}/{EmpresaId}/1/test", new { })).EnsureSuccessStatusCode();
        var after = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Put_con_version_obsoleta_retorna_409_sin_mutar()
    {
        var client = await CreateClientAsync(
            PermisosCanonicos.IntegracionesFiscalLeer,
            PermisosCanonicos.IntegracionesFiscalAdministrar);
        var saved = await PutCurrentAsync(client);
        saved.EnsureSuccessStatusCode();
        var version = (await ReadJsonAsync(saved)).GetProperty("version").GetInt32();

        var current = await PutWithVersionAsync(client, version, Body("https://live.fiscalapi.com"));
        current.EnsureSuccessStatusCode();
        var stale = await PutWithVersionAsync(client, version, Body("https://test.fiscalapi.com"));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CONCURRENCY_CONFLICT", (await ReadJsonAsync(stale)).GetProperty("code").GetString());
        var persisted = await client.GetAsync($"{Base}/{EmpresaId}/1");
        Assert.Equal("https://live.fiscalapi.com", (await ReadJsonAsync(persisted)).GetProperty("baseUrl").GetString());
    }

    [Fact]
    public async Task Credenciales_rechazadas_no_rotan_y_replay_no_vuelve_a_probar()
    {
        var probe = new StubSdk();
        await using var factory = FactoryWith(probe);
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalLeer,
            PermisosCanonicos.IntegracionesFiscalAdministrar, factory: factory);
        (await PutCurrentAsync(client, factory: factory)).EnsureSuccessStatusCode();
        var before = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();
        probe.Resultado = new(false, 401, "detalle secreto DEMO", 2, DateTimeOffset.UtcNow);
        var rejected = await PutCurrentAsync(client, new
            { baseUrl = "https://live.fiscalapi.com", apiKey = "DEMO-rechazada", activo = true }, factory);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var json = await rejected.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DEMO-rechazada", json);
        Assert.DoesNotContain("detalle secreto", json);
        var after = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();
        Assert.Equal(before, after);
        probe.Resultado = new(true, 200, "DEMO", 1, DateTimeOffset.UtcNow);
        var key = Guid.NewGuid().ToString("D");
        var first = await PutAsync(client, key);
        first.EnsureSuccessStatusCode();
        var calls = probe.PingCount;
        (await PutAsync(client, key)).EnsureSuccessStatusCode();
        Assert.Equal(calls, probe.PingCount);
    }

    [Fact]
    public async Task Probar_candidato_usa_key_nueva_sin_guardar()
    {
        var probe = new StubSdk();
        await using var factory = FactoryWith(probe);
        var client = await CreateClientAsync(PermisosCanonicos.IntegracionesFiscalLeer,
            PermisosCanonicos.IntegracionesFiscalAdministrar, factory: factory);
        var before = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();
        var response = await client.PostAsJsonAsync($"{Base}/{EmpresaId}/1/test", new
            { baseUrl = "https://test.fiscalapi.com", apiKey = "DEMO-candidata" });
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, probe.PingCount);
        var after = await (await client.GetAsync($"{Base}/{EmpresaId}/1")).Content.ReadAsStringAsync();
        Assert.Equal(before, after);
    }

    private WebApplicationFactory<Program> FactoryWith(StubSdk sdk) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IFiscalApiSdkClient>();
            services.AddSingleton<IFiscalApiSdkClient>(sdk);
            services.RemoveAll<IPacCandidatoProbe>();
            services.AddSingleton<IPacCandidatoProbe>(sdk);
        }));

    private async Task<HttpClient> CreateClientAsync(
        string? permiso1 = null,
        string? permiso2 = null,
        bool autoIdempotency = true,
        WebApplicationFactory<Program>? factory = null)
    {
        factory ??= _factory;
        var oid = $"adm09-{Guid.NewGuid():N}";
        _usuariosTemporales.Add(oid);
        if (permiso1 is not null)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var userId = Guid.CreateVersion7();
            var roleId = Guid.CreateVersion7();
            _rolesTemporales.Add(roleId);
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

    private static object Body(string baseUrl = "https://test.fiscalapi.com") => new
    {
        baseUrl,
        apiKey = "dummy-not-a-secret",
        activo = true,
    };

    private async Task<HttpResponseMessage> PutAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/{EmpresaId}/1")
        {
            Content = JsonContent.Create(Body()),
        };
        request.Headers.Add("Idempotency-Key", key);
        var version = await CurrentVersionAsync();
        if (version is int value) request.Headers.Add("X-Expected-Version", value.ToString());
        return await client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutCurrentAsync(
        HttpClient client, object? body = null, WebApplicationFactory<Program>? factory = null)
    {
        factory ??= _factory;
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/{EmpresaId}/1")
        {
            Content = JsonContent.Create(body ?? Body()),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        var version = await CurrentVersionAsync(factory);
        if (version is int value) request.Headers.Add("X-Expected-Version", value.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutWithVersionAsync(
        HttpClient client, int version, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{Base}/{EmpresaId}/1")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));
        request.Headers.Add("X-Expected-Version", version.ToString());
        return await client.SendAsync(request);
    }

    private async Task<int?> CurrentVersionAsync(WebApplicationFactory<Program>? factory = null)
    {
        factory ??= _factory;
        using var scope = factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<IntegracionesFiscalDbContext>()
            .ConfiguracionesPac.AsNoTracking()
            .Where(c => c.EmpresaId == EmpresaId && c.Proveedor == ProveedorPac.FiscalApi)
            .Select(c => (int?)c.Version)
            .SingleOrDefaultAsync();
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

    private sealed class StubSdk : IFiscalApiSdkClient, IPacCandidatoProbe
    {
        public Task<PingResultDto> ProbarAsync(string url, string key, CancellationToken ct)
        {
            PingCount++;
            return Task.FromResult(Resultado);
        }
        public int PingCount { get; private set; }
        public PingResultDto Resultado { get; set; } = new(true, 200, "Conexión disponible", 7, DateTimeOffset.UtcNow);
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
