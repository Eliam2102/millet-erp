using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Validacion;

/// <summary>
/// Tests integration de la validación cross-table que sobrevive en
/// <c>CrearRequisicionHandler</c>:
///
/// <list type="number">
///   <item><c>RQ_DEPTO_NO_OPERA_EN_SUCURSAL</c> (422) — la asignación
///         <c>(sucursal, depto)</c> no está Activa en
///         <c>compartido.sucursal_departamentos</c> (no existe o
///         inactiva).</item>
/// </list>
///
/// <para>
/// Almacén-por-línea PR3: se retiraron los casos <c>ALMACEN_NO_ENCONTRADO</c>
/// y <c>RQ_ALMACEN_NO_PERTENECE_A_SUCURSAL</c> — la RQ manual ya no captura
/// almacén (nace null), así que el handler dejó de validarlo.
/// </para>
///
/// <para>
/// El test positivo confirma que el happy path con
/// <c>TestComprasFixtures.BuildCrearRqValidBody()</c> default
/// (MID + COMPRAS) sigue retornando 201.
/// </para>
///
/// <para>
/// Para el caso <c>RQ_DEPTO_NO_OPERA_EN_SUCURSAL</c>, el test siembra
/// inline un departamento sin asignación a MID (mismo patrón
/// helper-privado que <c>BandejasEndpointsTests.SeedDeptoAisladoConAsignacionAsync</c>,
/// pero sin la 2ª inserción). No reusa el depto manual <c>SIS</c>
/// porque ese no es parte del seed canónico y no está garantizado en
/// DBs locales de otros devs.
/// </para>
/// </summary>
public class CrearRequisicionValidacionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/compras/requisiciones";

    // Empresa raíz de bootstrap (BootstrapSuperAdminHostedService.EmpresaInicialId):
    // el SuperAdmin de dev queda asignado a esta empresa (F1-ADM-01).
    private static readonly Guid EmpresaBootstrapId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public CrearRequisicionValidacionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Crear_Canonical_Retorna_201()
    {
        // Sanity check del happy path. Si ESTE falla, el Lote A introdujo
        // regresión real (las 3 validaciones cross-table rechazan el
        // fixture canónico que está sembrado en MID + COMPRAS + ALM-MID-G).
        var client = await CreateSuperAdminClientAsync();

        var response = await client.PostAsJsonAsync(
            EndpointBase,
            TestComprasFixtures.BuildCrearRqValidBody(
                descripcion: "PR-A2 Validacion happy path"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Crear_DeptoSinAsignacion_Retorna_422_RQ_DEPTO_NO_OPERA_EN_SUCURSAL()
    {
        // Siembra un depto en compartido.departamentos SIN crear la
        // asignación correspondiente en sucursal_departamentos. La
        // validación N:M rechaza la combinación con 422.
        var client = await CreateSuperAdminClientAsync();
        var deptoSinAsignar = await SeedDeptoSinAsignacionAsync();

        var response = await client.PostAsJsonAsync(
            EndpointBase,
            TestComprasFixtures.BuildCrearRqValidBody(
                departamentoId: deptoSinAsignar,
                descripcion: "PR-A2 Validacion depto sin asignacion"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(
            "RQ_DEPTO_NO_OPERA_EN_SUCURSAL",
            body.GetProperty("code").GetString());
    }

    // --- Helpers ---

    /// <summary>
    /// Inserta un departamento canónicamente válido en
    /// <c>compartido.departamentos</c> pero NO crea la asignación N:M
    /// correspondiente en <c>sucursal_departamentos</c>. Patrón calcado
    /// de <c>BandejasEndpointsTests.SeedDeptoAisladoConAsignacionAsync</c>
    /// pero sin la 2ª inserción — necesario para forzar el caso de
    /// "depto existe pero no opera en ninguna sucursal".
    /// </summary>
    private async Task<Guid> SeedDeptoSinAsignacionAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresaContext = scope.ServiceProvider
            .GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        var deptoId = Guid.CreateVersion7();
        // Guid.NewGuid (V4 totalmente random) para evitar colisiones
        // cuando los tests corren rápido (Guid.CreateVersion7 empieza con
        // timestamp en ms).
        var random = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpperInvariant();
        var clave = $"DEPT-NOASIG-{random.Substring(0, 8)}"; // ej. "DEPT-NOASIG-A1B2C3D4" — 20 chars (max).

        db.Departamentos.Add(new Departamento(
            id: deptoId,
            empresaId: EmpresaBootstrapId,
            clave: clave,
            nombre: $"Depto sin asignación {random}"));

        await db.SaveChangesAsync();
        return deptoId;
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
