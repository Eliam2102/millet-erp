using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.ClasificacionRq;

/// <summary>
/// 86e3tfedd: la RQ sólo acepta artículos de su clasificación, por HTTP real
/// (listado filtrado, agregar/editar línea y cambio de clasificación en cabecera).
/// </summary>
public class ArticulosPorClasificacionEndpointsTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const int Servicio = 0, OrdenCompra = 1, MateriaPrima = 2;

    private readonly StubsWebApplicationFactory _factory;

    public ArticulosPorClasificacionEndpointsTests(StubsWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Listado_tipoRequisicion_SoloDevuelveLosDeEseTipo()
    {
        var client = await CreateSuperAdminClientAsync();
        var prefijo = $"CLS{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        await SeedArticuloAsync(Naturaleza.Servicio, null, prefijo + "S");
        await SeedArticuloAsync(Naturaleza.Estandar, null, prefijo + "E");
        await SeedArticuloAsync(Naturaleza.Estandar, "Químicos", prefijo + "Q");

        Assert.Equal(new[] { prefijo + "S" }, await ClavesAsync(client, Servicio, prefijo));
        Assert.Equal(new[] { prefijo + "E" }, await ClavesAsync(client, OrdenCompra, prefijo));
        Assert.Equal(new[] { prefijo + "Q" }, await ClavesAsync(client, MateriaPrima, prefijo));
    }

    [Fact]
    public async Task AgregarLinea_ServicioEnRqDeMateriaPrima_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var servicio = await SeedArticuloAsync(Naturaleza.Servicio, null);
        var quimico = await SeedArticuloAsync(Naturaleza.Estandar, "Químicos");
        var rqId = await CrearRqAsync(client, MateriaPrima);

        var malo = await PostLineaAsync(client, rqId, servicio);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, malo.StatusCode);
        Assert.Contains("ARTICULO_NO_APLICA_A_CLASIFICACION", await malo.Content.ReadAsStringAsync());

        var ok = await PostLineaAsync(client, rqId, quimico);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
    }

    [Fact]
    public async Task ActualizarLinea_CambiarAArticuloDeOtroTipo_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var estandar = await SeedArticuloAsync(Naturaleza.Estandar, null);
        var servicio = await SeedArticuloAsync(Naturaleza.Servicio, null);
        var rqId = await CrearRqAsync(client, OrdenCompra);
        var alta = await PostLineaAsync(client, rqId, estandar);
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var lineaId = await IdAsync(alta);

        var resp = await client.PatchAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/lineas/{lineaId}", LineaBody(servicio));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("ARTICULO_NO_APLICA_A_CLASIFICACION", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EditarCabecera_CambiarClasificacionConPartidasDeOtroTipo_422()
    {
        var client = await CreateSuperAdminClientAsync();
        var estandar = await SeedArticuloAsync(Naturaleza.Estandar, null);
        var rqId = await CrearRqAsync(client, OrdenCompra);
        Assert.Equal(HttpStatusCode.Created, (await PostLineaAsync(client, rqId, estandar)).StatusCode);

        var resp = await client.PatchAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}", new { Clasificacion = Servicio });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.Contains("PARTIDAS_NO_APLICAN_A_CLASIFICACION", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EditarCabecera_CambiarClasificacionSinPartidas_204()
    {
        var client = await CreateSuperAdminClientAsync();
        var rqId = await CrearRqAsync(client, OrdenCompra);

        var resp = await client.PatchAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}", new { Clasificacion = Servicio });

        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    // --- Helpers ---

    private static async Task<string[]> ClavesAsync(HttpClient client, int tipo, string prefijo)
    {
        var resp = await client.GetAsync($"/api/v1/catalogos/articulos?tipoRequisicion={tipo}&clave={prefijo}&limit=50");
        resp.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("clave").GetString()!).OrderBy(c => c).ToArray();
    }

    private async Task<Guid> SeedArticuloAsync(Naturaleza naturaleza, string? categoria, string? clave = null)
    {
        var id = Guid.CreateVersion7();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        db.Articulos.Add(new Articulo(
            id: id,
            clave: clave ?? $"CLS{Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()}",
            nombre: $"Clasificación {naturaleza} {categoria}",
            unidadMedidaDefault: "PZA",
            naturaleza: naturaleza,
            categoria: categoria));
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task<Guid> CrearRqAsync(HttpClient client, int clasificacion)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(clasificacion: clasificacion, descripcion: "Test 86e3tfedd");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        crear.EnsureSuccessStatusCode();
        return await IdAsync(crear);
    }

    private static async Task<Guid> IdAsync(HttpResponseMessage resp)
    {
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private static object LineaBody(Guid articuloId) => new
    {
        ArticuloId = articuloId,
        Cantidad = 1m,
        UnidadMedida = "PZA",
        PrecioEstimadoMonto = 10m,
        PrecioEstimadoMoneda = "MXN",
        CuentaContableId = (Guid?)null,
        CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
        Proyecto = (string?)null,
        FechaRequerida = (DateOnly?)null,
        Notas = (string?)null,
    };

    private static Task<HttpResponseMessage> PostLineaAsync(HttpClient client, Guid rqId, Guid articuloId) =>
        client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", LineaBody(articuloId));

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = SuperAdminOid,
            Email = "superadmin@dev.local",
            Nombre = "Super Admin Dev",
            EmpresaId = (Guid?)null,
        });
        login.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
}
