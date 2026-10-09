using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// Tests integration del seed de los 9 roles MVP creados por
/// <c>BootstrapSuperAdminHostedService.EnsureRolesMvpAsync</c>
/// (F-Admin-PR3.3, A2 cerrada 2026-05-13; G1.9 / F1-ADM-05). El hosted service corre
/// al startup de la app y crea los roles idempotentemente.
///
/// <para>
/// La verificación se hace via el endpoint <c>GET /api/v1/identidad/roles</c>
/// del PR3.2 — si la lista incluye los 9 codigos esperados, el seed
/// funcionó.
/// </para>
/// </summary>
public class BootstrapRolesMvpTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string SuperAdminOid = "dev-superadmin";

    private static readonly string[] CodigosEsperados =
    [
        "super-admin",
        "admin-identidad",
        "admin-organizacional",
        "admin-catalogos",
        "admin-datos-maestros",
        "auditor",
        "admin-compras",
        "cxp",
        "tesoreria",
    ];

    private readonly WebApplicationFactory<Program> _factory;

    public BootstrapRolesMvpTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Bootstrap_Should_Seed_Nine_RolesMvp()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/v1/identidad/roles?limit=100");
        response.EnsureSuccessStatusCode();

        var body = await ReadJsonAsync(response);
        var codigos = body.GetProperty("items").EnumerateArray()
            .Select(r => r.GetProperty("codigo").GetString())
            .Where(c => c is not null)
            .ToHashSet();

        foreach (var esperado in CodigosEsperados)
        {
            Assert.Contains(esperado, codigos);
        }
    }

    [Fact]
    public async Task RolesMvp_Should_Be_EsDelSistema()
    {
        var client = await CreateSuperAdminClientAsync();

        var response = await client.GetAsync("/api/v1/identidad/roles?limit=100");
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);

        foreach (var rolJson in body.GetProperty("items").EnumerateArray())
        {
            var codigo = rolJson.GetProperty("codigo").GetString();
            if (codigo is null || !CodigosEsperados.Contains(codigo)) continue;

            var esDelSistema = rolJson.GetProperty("esDelSistema").GetBoolean();
            Assert.True(esDelSistema, $"El rol '{codigo}' debe tener EsDelSistema=true.");
        }
    }

    [Fact]
    public async Task RolesMvp_Should_HaveExpectedPermisos()
    {
        var client = await CreateSuperAdminClientAsync();

        // Auditor: debe tener al menos admin.auditoria.leer e infra.audit_log.leer.
        var auditorId = await GetRolIdByCodigoAsync(client, "auditor");
        var auditorDetalle = await GetDetalleAsync(client, auditorId);
        var auditorPermisoIds = auditorDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        Assert.NotEmpty(auditorPermisoIds);

        // Admin Compras: debe incluir compras.configuracion.leer y .editar.
        var comprasId = await GetRolIdByCodigoAsync(client, "admin-compras");
        var comprasDetalle = await GetDetalleAsync(client, comprasId);
        var comprasPermisoIds = comprasDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        // GUIDs deterministas del seed de Identidad — ver PermisosCanonicos.Todos.
        Assert.Contains(Guid.Parse("00000003-0004-0000-0000-000000000001"), comprasPermisoIds); // ConfiguracionLeer
        Assert.Contains(Guid.Parse("00000003-0004-0000-0000-000000000002"), comprasPermisoIds); // ConfiguracionEditar

        // Admin Organizacional: tras el re-sync del bootstrap debe tener el
        // permiso de gestión del N:M Sucursal↔Departamento (D2) y la lectura
        // de catálogos cross-empresa para el Sheet (D3). El re-sync es
        // aditivo y corre en StartAsync (await del host) → determinístico.
        var orgAdminId = await GetRolIdByCodigoAsync(client, "admin-organizacional");
        var orgAdminDetalle = await GetDetalleAsync(client, orgAdminId);
        var orgAdminPermisoIds = orgAdminDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        Assert.Contains(Guid.Parse("00000005-0006-0000-0000-000000000001"), orgAdminPermisoIds); // admin.sucursales.departamentos-gestionar (D2)
        Assert.Contains(Guid.Parse("00000004-0001-0000-0000-000000000001"), orgAdminPermisoIds); // compartido.catalogos.leer (D3)
        // Defensivo: el predicado está acotado — no debe arrastrar permisos de
        // Compras (StartsWith admin.* / == compartido.catalogos.leer, nada más).
        Assert.DoesNotContain(Guid.Parse("00000003-0004-0000-0000-000000000001"), orgAdminPermisoIds); // compras.configuracion.leer

        // Admin Catálogos: debe tener 8 permisos (2 compartido.catalogos.* + 6 catalogos.*)
        var catalogosId = await GetRolIdByCodigoAsync(client, "admin-catalogos");
        var catalogosDetalle = await GetDetalleAsync(client, catalogosId);
        var catalogosPermisoIds = catalogosDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        Assert.Contains(Guid.Parse("00000004-0001-0000-0000-000000000001"), catalogosPermisoIds); // compartido.catalogos.leer
        Assert.Contains(Guid.Parse("00000004-0002-0000-0000-000000000001"), catalogosPermisoIds); // compartido.catalogos.administrar
        Assert.Contains(Guid.Parse("00000004-0003-0000-0000-000000000001"), catalogosPermisoIds); // catalogos.monedas.gestionar
        Assert.Contains(Guid.Parse("00000004-0004-0000-0000-000000000001"), catalogosPermisoIds); // catalogos.tipos-cambio.gestionar
        Assert.Contains(Guid.Parse("00000004-0005-0000-0000-000000000001"), catalogosPermisoIds); // catalogos.condiciones-pago.gestionar
        Assert.Contains(Guid.Parse("00000004-0006-0000-0000-000000000001"), catalogosPermisoIds); // catalogos.incoterms.gestionar
        Assert.Contains(Guid.Parse("00000004-0007-0000-0000-000000000001"), catalogosPermisoIds); // catalogos.transportistas.gestionar
        Assert.Contains(Guid.Parse("00000004-0008-0000-0000-000000000001"), catalogosPermisoIds); // catalogos.unidades-medida.gestionar

        // Admin Datos Maestros: debe tener compartido.catalogos.* + datos_maestros.*
        var datosMaestrosId = await GetRolIdByCodigoAsync(client, "admin-datos-maestros");
        var datosMaestrosDetalle = await GetDetalleAsync(client, datosMaestrosId);
        var datosMaestrosPermisoIds = datosMaestrosDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();
        Assert.Contains(Guid.Parse("00000004-0001-0000-0000-000000000001"), datosMaestrosPermisoIds); // compartido.catalogos.leer
        Assert.Contains(Guid.Parse("00000004-0002-0000-0000-000000000001"), datosMaestrosPermisoIds); // compartido.catalogos.administrar
        Assert.Contains(Guid.Parse("00000004-0009-0000-0000-000000000001"), datosMaestrosPermisoIds); // datos_maestros.proveedores.gestionar
        Assert.Contains(Guid.Parse("00000004-0010-0000-0000-000000000001"), datosMaestrosPermisoIds); // datos_maestros.articulos.gestionar
        Assert.Contains(Guid.Parse("00000004-0011-0000-0000-000000000001"), datosMaestrosPermisoIds); // datos_maestros.clientes.gestionar
        Assert.Contains(Guid.Parse("00000004-0012-0000-0000-000000000001"), datosMaestrosPermisoIds); // datos_maestros.productos-aw.gestionar
    }

    [Fact]
    public async Task RolesCxpYTesoreria_Should_Tener_Permisos_Bancarios_Esperados()
    {
        var client = await CreateSuperAdminClientAsync();

        var bancariosVerId = Guid.Parse("00000004-0009-0000-0000-000000000002");
        var bancariosEditarId = Guid.Parse("00000004-0009-0000-0000-000000000003");
        var proveedoresGestionarId = Guid.Parse("00000004-0009-0000-0000-000000000001");
        var validarId = Guid.Parse("00000004-0009-0000-0000-000000000007");
        var catalogosAdministrarId = Guid.Parse("00000004-0002-0000-0000-000000000001");

        // cxp: bancarios-ver, validar, gestionar; NO bancarios-editar ni catalogos.administrar
        var cxpId = await GetRolIdByCodigoAsync(client, "cxp");
        var cxpDetalle = await GetDetalleAsync(client, cxpId);
        var cxpPermisoIds = cxpDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();

        Assert.Contains(bancariosVerId, cxpPermisoIds);
        Assert.Contains(validarId, cxpPermisoIds);
        Assert.Contains(Guid.Parse("00000004-0009-0000-0000-000000000008"), cxpPermisoIds);
        Assert.Contains(proveedoresGestionarId, cxpPermisoIds);
        Assert.DoesNotContain(bancariosEditarId, cxpPermisoIds);
        Assert.DoesNotContain(catalogosAdministrarId, cxpPermisoIds);

        // tesoreria: bancarios-ver, bancarios-editar, gestionar; NO catalogos.administrar ni validar
        var tesoreriaId = await GetRolIdByCodigoAsync(client, "tesoreria");
        var tesoreriaDetalle = await GetDetalleAsync(client, tesoreriaId);
        var tesoreriaPermisoIds = tesoreriaDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();

        Assert.Contains(bancariosVerId, tesoreriaPermisoIds);
        Assert.Contains(bancariosEditarId, tesoreriaPermisoIds);
        Assert.Contains(proveedoresGestionarId, tesoreriaPermisoIds);
        Assert.DoesNotContain(catalogosAdministrarId, tesoreriaPermisoIds);
        Assert.DoesNotContain(validarId, tesoreriaPermisoIds);
        Assert.DoesNotContain(Guid.Parse("00000004-0009-0000-0000-000000000008"), tesoreriaPermisoIds);

        // admin-datos-maestros: contiene bancarios-ver y NO bancarios-editar (H5 / V44)
        var datosMaestrosId = await GetRolIdByCodigoAsync(client, "admin-datos-maestros");
        var datosMaestrosDetalle = await GetDetalleAsync(client, datosMaestrosId);
        var datosMaestrosPermisoIds = datosMaestrosDetalle.GetProperty("permisoIds")
            .EnumerateArray()
            .Select(g => g.GetGuid())
            .ToHashSet();

        Assert.Contains(bancariosVerId, datosMaestrosPermisoIds);
        Assert.DoesNotContain(bancariosEditarId, datosMaestrosPermisoIds);
        Assert.DoesNotContain(Guid.Parse("00000004-0009-0000-0000-000000000008"), datosMaestrosPermisoIds);
    }

    [Fact]
    public async Task Bootstrap_SegundaEjecucion_NoDuplica_Roles_Ni_Permisos_De_Cxp_Y_Tesoreria()
    {
        // Fuerza el arranque del host (corre el bootstrap la primera vez).
        _ = await CreateSuperAdminClientAsync();

        async Task<(int Roles, int Permisos)> ContarAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var codigos = new[] { "cxp", "tesoreria", "admin-datos-maestros" };
            var roles = await db.Roles.AsNoTracking().Where(r => codigos.Contains(r.Codigo)).ToListAsync();
            var rolIds = roles.Select(r => r.Id).ToList();
            var permisos = await db.RolPermisos.AsNoTracking().CountAsync(rp => rolIds.Contains(rp.RolId));
            return (roles.Count, permisos);
        }

        var antes = await ContarAsync();
        Assert.Equal(3, antes.Roles);

        var bootstrap = _factory.Services.GetServices<IHostedService>()
            .OfType<BootstrapSuperAdminHostedService>().Single();
        await bootstrap.StartAsync(CancellationToken.None);

        var despues = await ContarAsync();
        Assert.Equal(antes, despues);
    }

    private static async Task<Guid> GetRolIdByCodigoAsync(HttpClient client, string codigo)
    {
        var response = await client.GetAsync("/api/v1/identidad/roles?limit=100");
        response.EnsureSuccessStatusCode();
        var body = await ReadJsonAsync(response);
        var rol = body.GetProperty("items").EnumerateArray()
            .First(r => r.GetProperty("codigo").GetString() == codigo);
        return rol.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> GetDetalleAsync(HttpClient client, Guid rolId)
    {
        var response = await client.GetAsync($"/api/v1/identidad/roles/{rolId}");
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync(response);
    }

    private async Task<HttpClient> CreateSuperAdminClientAsync()
    {
        var client = _factory.CreateClient();
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
