using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Tests integration del endpoint consolidado de auditoría
/// <c>GET /api/v1/admin/auditoria</c> (F-Admin-PR7.2). Cierra
/// <c>PLATFORM-TODO(&lt;AuditUI&gt;)</c>.
/// </summary>
public class AuditoriaEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/admin/auditoria";

    private readonly WebApplicationFactory<Program> _factory;

    public AuditoriaEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Sin_Rango_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        // ASP.NET model binder rechaza con 400 cuando falta un required [FromQuery] DateOnly.
        var response = await client.GetAsync(EndpointBase);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Con_Rango_Excediendo_90_Dias_Retorna_400()
    {
        var client = await CreateSuperAdminClientAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hace100 = hoy.AddDays(-100);
        var url = $"{EndpointBase}?desde={hace100:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}";

        var response = await client.GetAsync(url);

        // ValidationPipelineBehavior + GlobalExceptionHandler → 400
        // (convención del repo: validators = 400, invariantes domain = 422).
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_Con_Rango_Valido_Retorna_200_Con_Shape_Esperado()
    {
        var client = await CreateSuperAdminClientAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hace30 = hoy.AddDays(-30);
        var url = $"{EndpointBase}?desde={hace30:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}";

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("items", out var items));
        Assert.Equal(JsonValueKind.Array, items.ValueKind);
        Assert.True(body.TryGetProperty("total", out var total));
        Assert.True(total.GetInt32() >= 0);
    }

    [Fact]
    public async Task Get_Con_Zona_Horaria_Incluye_Acceso_Del_Dia_Local()
    {
        var client = await CreateSuperAdminClientAsync();
        var zona = TimeZoneInfo.FindSystemTimeZoneById("America/Merida");
        var hoyLocal = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zona).DateTime);
        var response = await client.GetAsync(
            $"{EndpointBase}?desde={hoyLocal:yyyy-MM-dd}&hasta={hoyLocal:yyyy-MM-dd}&zonaHoraria=America%2FMerida&recurso=Sesion");

        response.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(response)).GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item =>
            item.GetProperty("entidad").GetString() == "Sesion" &&
            item.GetProperty("operacion").GetString() == "acceso");
    }

    [Fact]
    public async Task Get_Filtra_Sucursal_De_La_Empresa_Activa()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"AUD-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var crear = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty,
            Clave = clave,
            Nombre = $"Sucursal {clave}",
        });
        crear.EnsureSuccessStatusCode();
        var sucursalId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var desde = hoy.AddDays(-1);
        var url = $"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&sucursalId={sucursalId}";

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await ReadJsonAsync(response)).GetProperty("items");
        Assert.Contains(items.EnumerateArray(), i =>
            i.GetProperty("entidad").GetString() == "Sucursal" &&
            i.GetProperty("sucursalId").GetGuid() == sucursalId &&
            i.GetProperty("sucursalClave").GetString() == clave);
        Assert.All(items.EnumerateArray(), i =>
            Assert.Equal(sucursalId, i.GetProperty("sucursalId").GetGuid()));

        var sinFiltroSucursal = await client.GetAsync(
            $"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, sinFiltroSucursal.StatusCode);
    }

    [Fact]
    public async Task Get_No_Expone_Eventos_Globales_Ni_De_Otra_Empresa()
    {
        var client = await CreateSuperAdminClientAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        var marcador = $"AISLAMIENTO-{Guid.NewGuid():N}";
        var empresaActual = Guid.Parse("00000003-0000-0000-0000-000000000001");
        var otraEmpresa = Guid.NewGuid();
        foreach (var empresa in new Guid?[] { null, otraEmpresa, empresaActual })
            db.AuditLog.Add(new AuditLogEntry
            {
                Id = Guid.CreateVersion7(), Timestamp = DateTimeOffset.UtcNow,
                EmpresaId = empresa, Modulo = "QA", Entidad = marcador,
                Operacion = "crear", Cambios = "{}", CorrelationId = Guid.NewGuid(),
            });
        await db.SaveChangesAsync();

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var url = $"{EndpointBase}?desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&recurso={marcador}";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(response)).GetProperty("items");
        Assert.Single(items.EnumerateArray());
        Assert.Equal(empresaActual, items[0].GetProperty("empresaId").GetGuid());

        var forzado = await client.GetAsync($"{url}&empresaId={otraEmpresa}");
        forzado.EnsureSuccessStatusCode();
        Assert.Empty((await ReadJsonAsync(forzado)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Get_Proyecta_Campos_Snapshot_Y_Ninguno_Vacio()
    {
        var client = await CreateSuperAdminClientAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var desde = hoy.AddDays(-30);
        var url = $"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&limit=50";

        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);

        foreach (var item in items)
        {
            Assert.True(item.TryGetProperty("actorNombre", out var actorNombre) && !string.IsNullOrWhiteSpace(actorNombre.GetString()));
            Assert.True(item.TryGetProperty("actorTipo", out var actorTipo) && !string.IsNullOrWhiteSpace(actorTipo.GetString()));
            Assert.True(item.TryGetProperty("entidadEtiqueta", out var etiqueta) && !string.IsNullOrWhiteSpace(etiqueta.GetString()));
            Assert.True(item.TryGetProperty("resumen", out var resumen) && !string.IsNullOrWhiteSpace(resumen.GetString()));
            // Compatibilidad frontend
            Assert.True(item.TryGetProperty("usuarioNombre", out var usuarioNombre) && !string.IsNullOrWhiteSpace(usuarioNombre.GetString()));
        }
    }

    [Fact]
    public async Task Get_Filtra_Por_ActorTipo_Y_Por_Q_Texto()
    {
        var client = await CreateSuperAdminClientAsync();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var desde = hoy.AddDays(-30);

        // Filtro por actorTipo=proceso
        var resProceso = await client.GetAsync($"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&actorTipo=proceso");
        Assert.Equal(HttpStatusCode.OK, resProceso.StatusCode);
        var bodyProceso = await ReadJsonAsync(resProceso);
        var itemsProceso = bodyProceso.GetProperty("items").EnumerateArray().ToList();
        if (itemsProceso.Count > 0)
        {
            Assert.All(itemsProceso, i => Assert.Equal("proceso", i.GetProperty("actorTipo").GetString()));
        }

        // Filtro por busqueda de texto Q
        var resQ = await client.GetAsync($"{EndpointBase}?desde={desde:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&q=Cre");
        Assert.Equal(HttpStatusCode.OK, resQ.StatusCode);
        var bodyQ = await ReadJsonAsync(resQ);
        var itemsQ = bodyQ.GetProperty("items").EnumerateArray().ToList();
        if (itemsQ.Count > 0)
        {
            Assert.All(itemsQ, i =>
            {
                var resumen = i.GetProperty("resumen").GetString() ?? "";
                var etiqueta = i.GetProperty("entidadEtiqueta").GetString() ?? "";
                var actor = i.GetProperty("actorNombre").GetString() ?? "";
                Assert.True(
                    resumen.Contains("Cre", StringComparison.OrdinalIgnoreCase) ||
                    etiqueta.Contains("Cre", StringComparison.OrdinalIgnoreCase) ||
                    actor.Contains("Cre", StringComparison.OrdinalIgnoreCase));
            });
        }
    }

    [Fact]
    public async Task Get_Sin_Token_Retorna_401()
    {
        var client = _factory.CreateClient();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var hace30 = hoy.AddDays(-30);
        var response = await client.GetAsync(
            $"{EndpointBase}?desde={hace30:yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Alta_Y_Cambio_De_Sucursal_Conservan_Usuario_Fecha_Y_Antes_Despues()
    {
        var client = await CreateSuperAdminClientAsync();
        var clave = $"AUD-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var nombreInicial = $"Sucursal {clave}";
        var crear = await client.PostAsJsonAsync("/api/v1/admin/empresas/sucursales", new
        {
            Id = Guid.Empty, Clave = clave, Nombre = nombreInicial,
        });
        crear.EnsureSuccessStatusCode();
        var id = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var nombreNuevo = $"{nombreInicial} editada";
        var editar = await client.PatchAsJsonAsync(
            $"/api/v1/admin/empresas/sucursales/{id}", new { Nombre = nombreNuevo });
        editar.EnsureSuccessStatusCode();

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var audit = await client.GetAsync(
            $"{EndpointBase}?desde={hoy.AddDays(-1):yyyy-MM-dd}&hasta={hoy:yyyy-MM-dd}&recurso=Sucursal");
        audit.EnsureSuccessStatusCode();
        var items = (await ReadJsonAsync(audit)).GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("entidadId").ValueKind == JsonValueKind.String &&
                i.GetProperty("entidadId").GetGuid() == id).ToList();
        Assert.Contains(items, i => i.GetProperty("operacion").GetString() == "crear");
        var cambio = Assert.Single(items, i => i.GetProperty("operacion").GetString() == "actualizar");
        Assert.Equal(JsonValueKind.String, cambio.GetProperty("usuarioId").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(cambio.GetProperty("usuarioNombre").GetString()));
        Assert.NotEqual(default, cambio.GetProperty("timestamp").GetDateTimeOffset());
        using var diff = JsonDocument.Parse(cambio.GetProperty("cambios").GetString()!);
        var nombre = diff.RootElement.GetProperty("diff").GetProperty("Nombre");
        Assert.Equal(nombreInicial, nombre.GetProperty("antes").GetString());
        Assert.Equal(nombreNuevo, nombre.GetProperty("despues").GetString());
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
