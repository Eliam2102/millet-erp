using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.IntegrationTests.Fixtures;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras;

/// <summary>
/// Alcance por sucursal (ADR-0051) al abrir una requisición (G1.11 / CA1.4): la sucursal se
/// deriva de <c>Requisicion.SucursalId</c> en BD, no del cliente.
/// </summary>
public class RequisicionesAlcanceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string EndpointBase = "/api/v1/compras/requisiciones";
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public RequisicionesAlcanceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Operativo_Asociado_Abre_RQ_De_Su_Sucursal()
    {
        var rqMid = await CrearRqEnSucursalAsync(TestComprasFixtures.SucursalMid);
        var (cliente, _) = await CreateOperativoAsync(
            TestComprasFixtures.SucursalMid, PermisosCanonicos.ComprasRequisicionesLeer);

        var resp = await cliente.GetAsync($"{EndpointBase}/{rqMid}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Operativo_De_Otra_Sucursal_Recibe_403_En_Detalle_Cubrimiento_E_Historico()
    {
        var rqMid = await CrearRqEnSucursalAsync(TestComprasFixtures.SucursalMid);
        var (cliente, _) = await CreateOperativoAsync(
            TestComprasFixtures.SucursalMty, PermisosCanonicos.ComprasRequisicionesLeer);

        await AssertForbiddenSucursalAsync(await cliente.GetAsync($"{EndpointBase}/{rqMid}"));
        await AssertForbiddenSucursalAsync(await cliente.GetAsync($"{EndpointBase}/{rqMid}/cubrimiento-estimado"));
        await AssertForbiddenSucursalAsync(await cliente.GetAsync($"{EndpointBase}/{rqMid}/historico"));
    }

    [Fact]
    public async Task Corporativo_Con_Bypass_Abre_RQ_Sin_Asociacion()
    {
        var rqMid = await CrearRqEnSucursalAsync(TestComprasFixtures.SucursalMid);
        var (cliente, _) = await CreateUsuarioConPermisosAsync(
            PermisosCanonicos.ComprasRequisicionesLeer,
            PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"{EndpointBase}/{rqMid}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync($"{EndpointBase}/{rqMid}/historico")).StatusCode);
    }

    [Fact]
    public async Task RQ_Inexistente_Retorna_404()
    {
        var (cliente, _) = await CreateUsuarioConPermisosAsync(
            PermisosCanonicos.ComprasRequisicionesLeer,
            PermisosCanonicos.ComprasRequisicionesLeerTodasSucursales);

        var resp = await cliente.GetAsync($"{EndpointBase}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Equal("REQUISICION_NO_ENCONTRADA", (await ReadJsonAsync(resp)).GetProperty("code").GetString());
    }

    // --- Helpers ---

    private async Task<Guid> CrearRqEnSucursalAsync(Guid sucursalId)
    {
        var admin = await CreateClientAsync(SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        var resp = await admin.PostAsJsonAsync(
            EndpointBase, TestComprasFixtures.BuildCrearRqValidBody(sucursalId: sucursalId));
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(resp)).GetProperty("id").GetGuid();
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateOperativoAsync(
        Guid sucursalId, params string[] permisoCodigos)
    {
        var (client, usuarioId) = await CreateUsuarioConPermisosAsync(permisoCodigos);
        using var scope = _factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(
            Guid.CreateVersion7(), usuarioId, sucursalId, EmpresaInicialId));
        await identidad.SaveChangesAsync();
        return (client, usuarioId);
    }

    private async Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioConPermisosAsync(
        params string[] permisoCodigos)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-rq-scope-{random}";
        var usuarioId = Guid.CreateVersion7();

        using (var scope = _factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-rq-scope-{random}", "Rol de prueba de alcance RQ"));
            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Alcance RQ"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        return (await CreateClientAsync(oid, $"{oid}@test.local", "Usuario Alcance RQ"), usuarioId);
    }

    private async Task<HttpClient> CreateClientAsync(string oid, string email, string nombre)
    {
        var client = _factory.CreateClientWithIdempotency();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new
        {
            EntraOid = oid,
            Email = email,
            Nombre = nombre,
            EmpresaId = (Guid?)null,
        });
        response.EnsureSuccessStatusCode();
        var token = (await ReadJsonAsync(response)).GetProperty("accessToken").GetString()
                    ?? throw new InvalidOperationException("fake-login no devolvió accessToken");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task AssertForbiddenSucursalAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("SUCURSAL_NO_ASOCIADA", (await ReadJsonAsync(response)).GetProperty("code").GetString());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }
}
