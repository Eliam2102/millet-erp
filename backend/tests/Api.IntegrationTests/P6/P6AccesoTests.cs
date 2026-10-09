using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Api.Auth;
using Millet.Api.Auth.Models;
using Millet.Identidad.Application.Ports;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.P6;

public sealed class P6AccesoTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inicio_recalcula_grupos_y_al_salir_revoca_rol_automatico(bool overage)
    {
        var oid = $"p6-grupos-{Guid.NewGuid():N}";
        var grupoId = Guid.NewGuid().ToString();
        var usuarioId = Guid.CreateVersion7();
        var rolGrupoId = Guid.CreateVersion7();
        var baseId = Guid.CreateVersion7();
        await using var limpieza = new Limpieza(factory, usuarioId, [baseId, rolGrupoId]);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            db.Roles.Add(new Rol(baseId, $"base-{oid}", "Base P6"));
            db.Roles.Add(new Rol(rolGrupoId, $"grupo-{oid}", "Grupo P6"));
            var permiso = await db.Permisos.SingleAsync(x => x.Codigo == PermisosCanonicos.IdentidadUsuariosLeer);
            db.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolGrupoId, permiso.Id));
            db.RolGruposEntraId.Add(new RolGrupoEntraId(Guid.CreateVersion7(), rolGrupoId, grupoId, "Grupo de prueba"));
            db.Usuarios.Add(new Usuario(usuarioId, oid, $"{oid}@test.local", "P6 Grupos"));
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuarioId, Empresa, baseId, null));
            await db.SaveChangesAsync();
        }
        var grupos = new GruposFake(grupoId);
        var validador = new ValidadorFake(oid, grupoId, overage);
        using var app = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IEntraTokenValidator>(); s.AddSingleton<IEntraTokenValidator>(validador);
            s.RemoveAll<IEntraGruposReadPort>(); s.AddSingleton<IEntraGruposReadPort>(grupos);
        }));
        var client = app.CreateClientWithIdempotency();
        var primera = await LoginAsync(client, "con-grupo");
        Assert.Contains(PermisosCanonicos.IdentidadUsuariosLeer, primera.Permisos);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primera.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/identidad/usuarios")).StatusCode);

        grupos.Ids = [];
        var siguiente = await LoginAsync(client, "sin-grupo");
        Assert.DoesNotContain(PermisosCanonicos.IdentidadUsuariosLeer, siguiente.Permisos);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", siguiente.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/identidad/usuarios")).StatusCode);
        Assert.Equal(overage ? 2 : 0, grupos.Consultas);
        using var verificacion = app.Services.CreateScope();
        var identidad = verificacion.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        Assert.Empty(await identidad.UsuarioGruposEntraId.Where(x => x.UsuarioId == usuarioId).ToListAsync());
        Assert.True(await identidad.UsuarioEmpresaRoles.IgnoreQueryFilters().AnyAsync(x => x.UsuarioId == usuarioId));
        // Volver al grupo calienta la caché; desactivar el rol debe revocarla sin otro login.
        grupos.Ids = [grupoId];
        var antesDeDesactivar = await LoginAsync(client, "con-grupo");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", antesDeDesactivar.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/identidad/usuarios")).StatusCode);
        using var admin = app.CreateClientWithIdempotency();
        var adminLogin = await admin.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = "dev-superadmin", Email = "superadmin@dev.local", Nombre = "Super Admin Dev" });
        adminLogin.EnsureSuccessStatusCode();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await adminLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/identidad/roles/{rolGrupoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/identidad/usuarios")).StatusCode);
    }

    [Fact]
    public async Task Administrador_organizacional_recibe_403_al_crear_o_desactivar_empresa()
    {
        var oid = $"p6-org-{Guid.NewGuid():N}";
        var usuarioId = Guid.CreateVersion7();
        await using var limpieza = new Limpieza(factory, usuarioId, []);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var rol = await db.Roles.SingleAsync(x => x.Codigo == "admin-organizacional");
            var usuario = new Usuario(usuarioId, oid, $"{oid}@test.local", "P6 Admin organización");
            db.Usuarios.Add(usuario);
            db.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuario.Id));
            db.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(Guid.CreateVersion7(), usuario.Id, Empresa, rol.Id, null));
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientWithIdempotency();
        var res = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = $"{oid}@test.local", Nombre = "P6 Admin organización" });
        res.EnsureSuccessStatusCode();
        var login = (await res.Content.ReadFromJsonAsync<LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/empresas", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/v1/admin/empresas/{Empresa}/desactivar", null)).StatusCode);
    }

    private sealed class Limpieza(WebApplicationFactory<Program> factory, Guid usuarioId, Guid[] roles) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            using var scope = factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            await db.UsuarioGruposEntraId.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await db.UsuarioEmpresaRoles.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await db.UsuarioPreferencias.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync();
            await db.Usuarios.Where(x => x.Id == usuarioId).ExecuteDeleteAsync();
            await db.RolGruposEntraId.Where(x => roles.Contains(x.RolId)).ExecuteDeleteAsync();
            await db.RolPermisos.Where(x => roles.Contains(x.RolId)).ExecuteDeleteAsync();
            await db.Roles.Where(x => roles.Contains(x.Id)).ExecuteDeleteAsync();
        }
    }
    private static async Task<LoginResponse> LoginAsync(HttpClient client, string token)
    {
        var response = await client.PostAsJsonAsync("/api/auth/sesion", new LoginRequest(token, Empresa));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
    private sealed class ValidadorFake(string oid, string grupoId, bool overage) : IEntraTokenValidator
    {
        public Task<EntraTokenClaims> ValidateAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult(new EntraTokenClaims(oid, $"{oid}@test.local", "P6 Grupos",
                overage || accessToken == "sin-grupo" ? [] : [grupoId], overage));
        public Task<ValidatedServicePrincipalToken?> TryValidateServicePrincipalAsync(string accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult<ValidatedServicePrincipalToken?>(null);
    }
    private sealed class GruposFake(string grupoId) : IEntraGruposReadPort
    {
        public IReadOnlyList<string> Ids { get; set; } = [grupoId];
        public int Consultas { get; private set; }
        public Task<IReadOnlyList<string>> ListarAsync(string objectId, CancellationToken ct)
        { Consultas++; return Task.FromResult(Ids); }
    }
}
