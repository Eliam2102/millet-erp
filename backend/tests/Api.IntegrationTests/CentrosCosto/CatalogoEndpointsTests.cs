using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.CentrosCosto.Application.PublicPorts;
using Millet.CentrosCosto.Domain;
using Millet.CentrosCosto.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CentrosCosto;

// Cambia los permisos en caché de dev-superadmin: corre sola, igual que ADM-08 (#76).
[Collection(ADM08SinParalelo.Nombre)]
public sealed class CatalogoEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Catalogo = "/api/v1/centros-costo";
    private readonly WebApplicationFactory<Program> _factory;

    public CatalogoEndpointsTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("GET", "/dim1")]
    [InlineData("POST", "/dim1")]
    [InlineData("GET", "/dim2")]
    [InlineData("POST", "/dim2")]
    [InlineData("GET", "/dim3")]
    [InlineData("POST", "/dim3")]
    [InlineData("PATCH", "/dim3/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/dim3/00000000-0000-0000-0000-000000000001/desactivar")]
    [InlineData("GET", "/jerarquia")]
    [InlineData("GET", "/dim3/buscar")]
    [InlineData("GET", "/asignaciones/00000000-0000-0000-0000-000000000001/arbol")]
    [InlineData("POST", "/asignaciones/00000000-0000-0000-0000-000000000001/marcar")]
    public async Task Api_exige_autenticacion_y_permiso(string method, string path)
    {
        using var client = _factory.CreateClientWithIdempotency();
        using var anonymous = await SendAsync(client, method, Catalogo + path, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await LoginAsync(client, "test-no-perms");
        using var forbidden = await SendAsync(client, method, Catalogo + path, new { });
        await AssertProblemAsync(forbidden, HttpStatusCode.Forbidden, "PERMISO_FALTANTE");
    }

    [Fact]
    public async Task Ciclo_http_valida_jerarquia_version_y_baja_con_lectura_historica()
    {
        using var client = _factory.CreateClientWithIdempotency();
        var login = await LoginAsync(client, "dev-superadmin");
        var userId = login.GetProperty("usuario").GetProperty("id").GetGuid();
        var empresaId = login.GetProperty("empresas").EnumerateArray()
            .Single(e => e.GetProperty("esLaActual").GetBoolean()).GetProperty("id").GetGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CentrosCostoDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        Guid? dim1Id = null, grupo2Id = null, grupo3Id = null;

        try
        {
            grupo2Id = (await CreateAsync(client, "/grupos-dim2", new { nombre = $"ADM08-G2-{suffix}" })).GetProperty("id").GetGuid();
            grupo3Id = (await CreateAsync(client, "/grupos-dim3", new { nombre = $"ADM08-G3-{suffix}" })).GetProperty("id").GetGuid();
            dim1Id = (await CreateAsync(client, "/dim1", new { clave = $"A{suffix}", nombre = "ADM08 nivel 1" })).GetProperty("id").GetGuid();
            var dim2 = await CreateAsync(client, "/dim2", new { dim1Id, clave = $"B{suffix}", nombre = "ADM08 nivel 2", grupoDim2Id = grupo2Id });
            var dim2Id = dim2.GetProperty("id").GetGuid();
            var body = new { dim2Id, clave = $"C{suffix}", nombre = $"ADM08 {suffix}", grupoDim3Id = grupo3Id };
            var created = await CreateAsync(client, "/dim3", body);
            var id = created.GetProperty("id").GetGuid();
            var path = $"{Catalogo}/dim3/{id}";
            var other = await CreateAsync(client, "/dim3", new
            {
                dim2Id, clave = $"D{suffix}", nombre = $"Otro {suffix}", grupoDim3Id = grupo3Id,
            });
            db.Asignaciones.Add(Asignacion.Crear(userId, id));
            await db.SaveChangesAsync();

            // El cache prepara los permisos del usuario; la autorizacion HTTP
            // y el evaluador de alcance usan sus implementaciones reales.
            var permissions = scope.ServiceProvider.GetRequiredService<IPermissionCache>();
            try
            {
                await permissions.SetAsync(userId, empresaId, new[] { PermisosCanonicos.CentrosCostoCatalogoLeer });
                using var scoped = await client.GetAsync($"{Catalogo}/dim3/buscar?q={suffix}");
                scoped.EnsureSuccessStatusCode();
                Assert.Equal(id, Assert.Single((await JsonAsync(scoped)).EnumerateArray()).GetProperty("id").GetGuid());

                foreach (var proxy in new[] { "/api/v1/compras/ordenes/dim3/buscar", "/api/v1/almacen/salidas/dim3/buscar" })
                {
                    using var denied = await client.GetAsync(proxy);
                    await AssertProblemAsync(denied, HttpStatusCode.Forbidden, "PERMISO_FALTANTE");
                }

                await permissions.SetAsync(userId, empresaId, new[]
                {
                    PermisosCanonicos.CentrosCostoCatalogoLeer, PermisosCanonicos.CentrosCostoDim3LeerTodos,
                });
                using var unrestricted = await client.GetAsync($"{Catalogo}/dim3/buscar?q={suffix}");
                unrestricted.EnsureSuccessStatusCode();
                Assert.Contains((await JsonAsync(unrestricted)).EnumerateArray(), item => item.GetProperty("id").GetGuid() == other.GetProperty("id").GetGuid());
            }
            finally
            {
                await permissions.InvalidateAsync(userId, empresaId);
            }

            using var duplicate = await client.PostAsJsonAsync(Catalogo + "/dim3", body);
            await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "CECO_CLAVE_DUPLICADA");
            using var invalidParent = await client.PostAsJsonAsync(Catalogo + "/dim3", new
            {
                dim2Id = Guid.NewGuid(), clave = $"X{suffix}", nombre = "Padre inexistente", grupoDim3Id = grupo3Id,
            });
            await AssertProblemAsync(invalidParent, HttpStatusCode.NotFound, "CECO_DIM2_NO_ENCONTRADA");

            using var detail = await client.GetAsync(path);
            detail.EnsureSuccessStatusCode();
            var etag = detail.Headers.ETag!.ToString();
            Assert.Equal($"\"{created.GetProperty("version").GetInt32()}\"", etag);
            var editBody = new { body.clave, nombre = $"Editado {suffix}", body.grupoDim3Id };
            using var missingVersion = await SendAsync(client, "PATCH", path, editBody);
            Assert.Equal(HttpStatusCode.PreconditionRequired, missingVersion.StatusCode);
            using var edited = await SendAsync(client, "PATCH", path, editBody, etag);
            edited.EnsureSuccessStatusCode();
            var newEtag = edited.Headers.ETag!.ToString();
            Assert.NotEqual(etag, newEtag);
            using var conflict = await SendAsync(client, "PATCH", path, editBody, etag);
            await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "CONCURRENCY_CONFLICT");

            using var disabled = await SendAsync(client, "POST", path + "/desactivar", new { }, newEtag);
            disabled.EnsureSuccessStatusCode();
            using var historical = await client.GetAsync(path);
            historical.EnsureSuccessStatusCode();
            Assert.Equal(id, (await JsonAsync(historical)).GetProperty("id").GetGuid());
            var readPort = scope.ServiceProvider.GetRequiredService<IDim3ReadPort>();
            var references = await readPort.ObtenerAsync(new[] { id }, CancellationToken.None);
            Assert.False(references[id].Activa);
            Assert.True(await db.Asignaciones.AsNoTracking().AnyAsync(a => a.UsuarioId == userId && a.Dim3Id == id));

            foreach (var selector in new[]
            {
                Catalogo + "/dim3/buscar",
                "/api/v1/compras/ordenes/dim3/buscar",
                "/api/v1/almacen/salidas/dim3/buscar",
            })
            {
                using var selection = await client.GetAsync($"{selector}?q={suffix}");
                selection.EnsureSuccessStatusCode();
                Assert.DoesNotContain((await JsonAsync(selection)).EnumerateArray(), item => item.GetProperty("id").GetGuid() == id);
            }

            using var disabledParent = await SendAsync(client, "POST", $"{Catalogo}/dim2/{dim2Id}/desactivar", new { }, $"\"{dim2.GetProperty("version").GetInt32()}\"");
            disabledParent.EnsureSuccessStatusCode();
            using var inactiveParent = await client.PostAsJsonAsync(Catalogo + "/dim3", new
            {
                dim2Id, clave = $"Y{suffix}", nombre = "Padre inactivo", grupoDim3Id = grupo3Id,
            });
            await AssertProblemAsync(inactiveParent, HttpStatusCode.UnprocessableEntity, "CECO_PADRE_INACTIVO");
        }
        finally
        {
            if (dim1Id is not null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.asignaciones WHERE dim3_id IN (SELECT id FROM centros_costo.dim3 WHERE dim2_id IN (SELECT id FROM centros_costo.dim2 WHERE dim1_id = {dim1Id}))");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim3 WHERE dim2_id IN (SELECT id FROM centros_costo.dim2 WHERE dim1_id = {dim1Id})");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim2 WHERE dim1_id = {dim1Id}");
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.dim1 WHERE id = {dim1Id}");
            }
            if (grupo2Id is not null)
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim2 WHERE id = {grupo2Id}");
            if (grupo3Id is not null)
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM centros_costo.grupos_dim3 WHERE id = {grupo3Id}");
        }
    }

    private static async Task<JsonElement> LoginAsync(HttpClient client, string oid)
    {
        using var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            entraOid = oid, email = $"{oid}@dev.local", nombre = oid, empresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var login = await JsonAsync(response);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        return login;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(Catalogo + path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        return await JsonAsync(response);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path, object body, string? etag = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET") request.Content = JsonContent.Create(body);
        if (etag is not null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        return await client.SendAsync(request);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await JsonAsync(response)).GetProperty("code").GetString());
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
