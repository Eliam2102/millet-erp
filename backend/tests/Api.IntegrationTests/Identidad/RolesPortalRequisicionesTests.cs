using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Identidad;

/// <summary>
/// REQ-PERM (86e3xd4uz): los 6 roles del Portal de Requisiciones. Solo CI (regla del owner 30-oct).
/// </summary>
public class RolesPortalRequisicionesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly string[] Codigos =
    [
        "requisitante", "requisitante-a-nombre-de-otros", "autorizador-requisiciones",
        "almacen-salida-contra-requisicion", "almacen-vale-urgente", "cancelacion-requisicion-autorizada",
    ];

    [Fact]
    public async Task Bootstrap_dos_veces_deja_seis_roles_sin_duplicados()
    {
        _ = factory.CreateClient(); // fuerza el arranque del host
        async Task<int> ContarAsync()
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            return await db.Roles.AsNoTracking().CountAsync(r => Codigos.Contains(r.Codigo));
        }

        Assert.Equal(6, await ContarAsync());
        var bootstrap = factory.Services.GetServices<IHostedService>().OfType<BootstrapSuperAdminHostedService>().Single();
        await bootstrap.StartAsync(CancellationToken.None);
        Assert.Equal(6, await ContarAsync());
    }

    [Fact]
    public async Task Requisitante_que_intenta_autorizar_recibe_403()
    {
        var client = await ClienteConRolAsync("requisitante");

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{Guid.NewGuid()}/autorizaciones", new { nivel = 1, notas = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requisitante_no_puede_registrar_vale_urgente()
    {
        var client = await ClienteConRolAsync("requisitante");

        using var response = await client.PostAsJsonAsync("/api/v1/almacen/salidas/vale", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<HttpClient> ClienteConRolAsync(string codigoRol)
    {
        var oid = $"test-portal-rq-{Guid.NewGuid():N}";
        var usuarioId = Guid.CreateVersion7();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rolId = await db.Roles.AsNoTracking().Where(r => r.Codigo == codigoRol).Select(r => r.Id).SingleAsync();
            db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "Usuario Portal RQ"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = $"{oid}@test.local", Nombre = "Usuario Portal RQ", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
}
