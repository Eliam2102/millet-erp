using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Decimales;

/// <summary>
/// Tests E2E de la validación de decimales por unidad (ADR-0046 Etapa 2) a
/// través del endpoint de captura de línea de RQ. Prueban el cableado real
/// completo: DI → <c>UnidadMedidaReadAdapter</c> (JOIN articulos⨝unidades_medida
/// en Postgres) → <c>DecimalesUnidadGuard</c> → <c>422</c> por HTTP.
///
/// <para>
/// El fix raíz: "pieza" (decimales=0) ahora rechaza <c>1.5</c>. Las demás
/// operaciones de Compras (OC manual/editar, recepción, desde-RQ) invocan el
/// MISMO guard compartido — cubiertas por los unit tests de
/// <c>DecimalesUnidadGuard</c> + este E2E del cableado.
/// </para>
/// </summary>
public class DecimalesPorUnidadEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";

    // Seed de unidades (ADR-0046): PZA = 0 decimales, L = 3 decimales.
    private static readonly Guid PzaId = Guid.Parse("00000002-0007-0000-0000-000000000001");
    private static readonly Guid LId = Guid.Parse("00000002-0007-0000-0000-000000000005");

    private readonly StubsWebApplicationFactory _factory;

    public DecimalesPorUnidadEndpointsTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Pieza_CantidadConDecimal_Rechaza_422_ConCodigo()
    {
        var client = await CreateSuperAdminClientAsync();
        var articuloId = await SeedArticuloAsync(PzaId, "PZA");
        var rqId = await CrearRqAsync(client);

        var resp = await PostLineaAsync(client, rqId, articuloId, cantidad: 1.5m, unidad: "PZA");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var cuerpo = await resp.Content.ReadAsStringAsync();
        Assert.Contains("CANTIDAD_DECIMALES_EXCEDE_UNIDAD", cuerpo);
    }

    [Fact]
    public async Task Pieza_CantidadEntera_Acepta_201()
    {
        var client = await CreateSuperAdminClientAsync();
        var articuloId = await SeedArticuloAsync(PzaId, "PZA");
        var rqId = await CrearRqAsync(client);

        var resp = await PostLineaAsync(client, rqId, articuloId, cantidad: 2m, unidad: "PZA");

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task Litro_TresDecimales_Acepta_PeroCuatro_Rechaza()
    {
        var client = await CreateSuperAdminClientAsync();
        var articuloId = await SeedArticuloAsync(LId, "L");
        var rqId = await CrearRqAsync(client);

        var ok = await PostLineaAsync(client, rqId, articuloId, cantidad: 1.250m, unidad: "L");
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

        var malo = await PostLineaAsync(client, rqId, articuloId, cantidad: 1.2505m, unidad: "L");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malo.StatusCode);
        Assert.Contains("CANTIDAD_DECIMALES_EXCEDE_UNIDAD", await malo.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ArticuloLegacy_SinFk_Permite_CantidadConDecimal()
    {
        var client = await CreateSuperAdminClientAsync();
        // Artículo sin unidad de catálogo (FK NULL) → la regla no aplica.
        var articuloId = await SeedArticuloAsync(unidadId: null, codigoUnidad: "CAJA");
        var rqId = await CrearRqAsync(client);

        var resp = await PostLineaAsync(client, rqId, articuloId, cantidad: 1.5m, unidad: "CAJA");

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    // --- Helpers ---

    private async Task<Guid> SeedArticuloAsync(Guid? unidadId, string codigoUnidad)
    {
        var id = Guid.CreateVersion7();
        var clave = $"DEC{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}";
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var empresaContext = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>();
        using var bypass = empresaContext.Bypass();

        db.Articulos.Add(new Articulo(
            id: id,
            clave: clave,
            nombre: $"Decimales {codigoUnidad}",
            unidadMedidaDefault: codigoUnidad,
            unidadMedidaId: unidadId));
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> CrearRqAsync(HttpClient client)
    {
        var rqBody = TestComprasFixtures.BuildCrearRqValidBody(descripcion: "Test E2E decimales");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", rqBody);
        crear.EnsureSuccessStatusCode();
        var json = await crear.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostLineaAsync(
        HttpClient client, Guid rqId, Guid articuloId, decimal cantidad, string unidad)
    {
        var body = new
        {
            ArticuloId = articuloId,
            Cantidad = cantidad,
            UnidadMedida = unidad,
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)null,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        };
        return client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", body);
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
        var json = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.GetProperty("accessToken").GetString()
               ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
    }
}
