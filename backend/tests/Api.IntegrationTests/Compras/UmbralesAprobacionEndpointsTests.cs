using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Tests integration de <c>GET/PUT /api/v1/compras/umbrales-aprobacion</c>
/// (umbrales N1/N2 por departamento). Vive en Api.IntegrationTests por la
/// misma razón que <see cref="ComprasSettingsEndpointsTests"/>.
/// </summary>
public class UmbralesAprobacionEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/compras/umbrales-aprobacion";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public UmbralesAprobacionEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_Crea_Umbral_Y_Get_Lo_Devuelve()
    {
        var client = await CreateSuperAdminClientAsync();
        var departamentoId = Guid.NewGuid();

        var put = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = departamentoId,
            UmbralMenorCuantia = 1000m,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });
        put.EnsureSuccessStatusCode();

        var get = await client.GetAsync(EndpointBase);
        get.EnsureSuccessStatusCode();
        var fila = (await ReadJsonAsync(get)).EnumerateArray()
            .Single(e => e.GetProperty("departamentoId").GetGuid() == departamentoId);
        Assert.Equal(1000m, fila.GetProperty("umbralMenorCuantia").GetDecimal());
        Assert.Equal(10000m, fila.GetProperty("umbralMonto").GetDecimal());
    }

    [Fact]
    public async Task Put_Con_Vigencia_De_Dia_Previo_Versiona_Y_Get_Devuelve_La_Nueva()
    {
        var client = await CreateSuperAdminClientAsync();
        var empresaId = await GetEmpresaIdAsync(client);
        var departamentoId = Guid.NewGuid();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        // Vigencia previa de hace 3 días, sembrada directo en BD.
        await SembrarVigenteAsync(empresaId, departamentoId, hoy.AddDays(-3), 5000m);

        var put = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = departamentoId,
            UmbralMenorCuantia = 2000m,
            UmbralMonto = 20000m,
            Moneda = "MXN",
        });
        put.EnsureSuccessStatusCode();

        var get = await client.GetAsync(EndpointBase);
        get.EnsureSuccessStatusCode();
        var fila = (await ReadJsonAsync(get)).EnumerateArray()
            .Single(e => e.GetProperty("departamentoId").GetGuid() == departamentoId);
        Assert.Equal(20000m, fila.GetProperty("umbralMonto").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var filas = await db.UmbralesAprobacionDepartamento
            .Where(u => u.DepartamentoId == departamentoId)
            .OrderBy(u => u.VigenteDesde)
            .ToListAsync();
        Assert.Equal(2, filas.Count);
        Assert.Equal(hoy.AddDays(-1), filas[0].VigenteHasta);
        Assert.Equal(hoy, filas[1].VigenteDesde);
        Assert.Null(filas[1].VigenteHasta);
    }

    [Fact]
    public async Task Put_Con_N1_Mayor_O_Igual_A_N2_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = 10000m,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Sin_Permiso_ConfiguracionLeer_Retorna403()
    {
        var (cliente, _) = await CreateUsuarioConPermisosAsync(PermisosCanonicos.ComprasRequisicionesLeer);

        var response = await cliente.GetAsync(EndpointBase);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_Sin_Permiso_ConfiguracionEditar_Retorna403_Aunque_Get_Si_Esta_Permitido()
    {
        var (cliente, _) = await CreateUsuarioConPermisosAsync(PermisosCanonicos.ComprasConfiguracionLeer);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync(EndpointBase)).StatusCode);

        var put = await cliente.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = 1000m,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task Put_Sin_IdempotencyKey_Retorna400()
    {
        var token = await FakeLoginAsync(_factory.CreateClientWithIdempotency(), SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        var client = _factory.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var put = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = 1000m,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Put_UmbralMontoNegativo_Retorna400()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = (decimal?)null,
            UmbralMonto = -1m,
            Moneda = "MXN",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UmbralMenorCuantiaNegativo_Retorna400()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = -1m,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_DepartamentoSinUmbral_NoRompe_Y_No_Aparece_En_La_Lista()
    {
        var client = await CreateSuperAdminClientAsync();
        var departamentoSinUmbral = Guid.NewGuid();

        var get = await client.GetAsync(EndpointBase);

        get.EnsureSuccessStatusCode();
        var filas = (await ReadJsonAsync(get)).EnumerateArray().ToList();
        Assert.DoesNotContain(filas, e => e.GetProperty("departamentoId").GetGuid() == departamentoSinUmbral);
    }

    [Fact]
    public async Task Put_Sin_Modo_Guarda_SoloDisponibilidad_Y_Minimo_Nulo()
    {
        var client = await CreateSuperAdminClientAsync();
        var departamentoId = Guid.NewGuid();

        var put = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = departamentoId,
            UmbralMenorCuantia = (decimal?)null,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });
        put.EnsureSuccessStatusCode();

        var fila = await GetFilaAsync(client, departamentoId);
        Assert.Equal("SoloDisponibilidad", fila.GetProperty("modoNivel2").GetString());
        Assert.Equal(JsonValueKind.Null, fila.GetProperty("montoMinimoDisponibilidad").ValueKind);
    }

    [Fact]
    public async Task Put_Con_Modo_Y_Minimo_Persiste_Y_Get_Los_Devuelve()
    {
        var client = await CreateSuperAdminClientAsync();
        var departamentoId = Guid.NewGuid();

        var put = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = departamentoId,
            UmbralMenorCuantia = (decimal?)null,
            UmbralMonto = 10000m,
            Moneda = "MXN",
            ModoNivel2 = "SoloDisponibilidad",
            MontoMinimoDisponibilidad = 3000m,
        });
        put.EnsureSuccessStatusCode();

        var fila = await GetFilaAsync(client, departamentoId);
        Assert.Equal("SoloDisponibilidad", fila.GetProperty("modoNivel2").GetString());
        Assert.Equal(3000m, fila.GetProperty("montoMinimoDisponibilidad").GetDecimal());
    }

    [Fact]
    public async Task Put_Con_Minimo_Negativo_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = (decimal?)null,
            UmbralMonto = 10000m,
            Moneda = "MXN",
            MontoMinimoDisponibilidad = -1m,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_Deja_Registro_En_Audit_Log()
    {
        var client = await CreateSuperAdminClientAsync();
        var antes = await ContarAuditoriaAsync();

        var put = await client.PutAsJsonAsync(EndpointBase, new
        {
            DepartamentoId = Guid.NewGuid(),
            UmbralMenorCuantia = (decimal?)null,
            UmbralMonto = 10000m,
            Moneda = "MXN",
        });
        put.EnsureSuccessStatusCode();

        Assert.True(await ContarAuditoriaAsync() > antes);
    }

    [Fact]
    public async Task Put_Concurrente_Mismo_Depto_Nunca_Devuelve_500()
    {
        var client = await CreateSuperAdminClientAsync();
        var departamentoId = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(1, 5).Select(async i =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, EndpointBase)
            {
                Content = JsonContent.Create(new
                {
                    DepartamentoId = departamentoId,
                    UmbralMenorCuantia = (decimal?)null,
                    UmbralMonto = 10000m + i,
                    Moneda = "MXN",
                }),
            };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            using var response = await client.SendAsync(request);
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));

        Assert.All(respuestas, r => Assert.True(
            r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"{r.StatusCode}: {r.Body}"));
        Assert.Contains(respuestas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict),
            r => Assert.Contains("UMBRAL_GUARDADO_CONCURRENTE", r.Body));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(1, await db.UmbralesAprobacionDepartamento
            .CountAsync(u => u.DepartamentoId == departamentoId && u.VigenteDesde == hoy));
    }

    private async Task<int> ContarAuditoriaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        return await db.Set<AuditLogEntry>().CountAsync(a => a.Entidad == "UmbralAprobacionDepartamento");
    }

    private static async Task<JsonElement> GetFilaAsync(HttpClient client, Guid departamentoId)
    {
        var get = await client.GetAsync(EndpointBase);
        get.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(get)).EnumerateArray()
            .Single(e => e.GetProperty("departamentoId").GetGuid() == departamentoId);
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioConPermisosAsync(params string[] permisoCodigos)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-umbral-{random}";
        var email = $"{oid}@test.local";
        var usuarioId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-umbral-{random}", "Rol de prueba de umbrales"));
            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, email, "Usuario Umbrales"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, email, "Usuario Umbrales");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, usuarioId);
    }

    private async Task SembrarVigenteAsync(Guid empresaId, Guid departamentoId, DateOnly desde, decimal monto)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        db.UmbralesAprobacionDepartamento.Add(
            new UmbralAprobacionDepartamento(empresaId, departamentoId, desde, monto));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> GetEmpresaIdAsync(HttpClient client)
    {
        var me = await client.GetAsync("/api/auth/me");
        me.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(me);
        return body.GetProperty("comprasSettings").GetProperty("empresaId").GetGuid();
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
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
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
