using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Autorizacion;

/// <summary>
/// Helpers compartidos de los tests de autorización (masiva, pendientes por
/// sucursal y modo de nivel 2): seeding de usuarios con permisos y sucursales
/// contra Postgres real, login fake y creación de requisiciones por HTTP.
/// Mismo patrón que <c>Api.IntegrationTests/Compras/RequisicionesAlcanceTests</c>.
/// </summary>
internal static class AutorizacionTestSeeding
{
    internal const string SuperAdminOid = "dev-superadmin";

    // Empresa inicial seedeada por BootstrapSuperAdminHostedService.
    internal static readonly Guid EmpresaInicialId = Guid.Parse("00000003-0000-0000-0000-000000000001");

    // Artículos sembrados (ver Matriz/EvaluadorMultidimensionalTests).
    internal static readonly Guid ArticuloEstandar = Guid.Parse("00000005-0002-0000-0000-000000000001");
    internal static readonly Guid ArticuloServicio = Guid.Parse("00000005-0002-0000-0000-000000000003");

    // Artículo de inventario con stock 0 (StubsWebApplicationFactory: ratio 0.0).
    internal static readonly Guid ArticuloSinStock = Guid.Parse("00000000-0000-0000-0000-000000000bbb");

    internal static async Task<HttpClient> CreateSuperAdminClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, SuperAdminOid, "superadmin@dev.local", "Super Admin Dev");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Cliente autenticado SIN el handler que añade <c>Idempotency-Key</c>, para
    /// probar el rechazo de mutaciones sin la cabecera.
    /// </summary>
    internal static async Task<HttpClient> CreateClientSinIdempotencyAsync(
        WebApplicationFactory<Program> factory, string oid, string email, string nombre)
    {
        var token = await FakeLoginAsync(factory.CreateClientWithIdempotency(), oid, email, nombre);
        var client = factory.CreateDefaultClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Crea un usuario de prueba con un rol que tiene exactamente los permisos
    /// indicados (sin sucursales asociadas) y devuelve su cliente autenticado.
    /// </summary>
    internal static async Task<(HttpClient Client, Guid UsuarioId)> CreateUsuarioConPermisosAsync(
        WebApplicationFactory<Program> factory, params string[] permisoCodigos)
    {
        var random = Guid.NewGuid().ToString("N")[..10];
        var oid = $"test-autz-{random}";
        var email = $"{oid}@test.local";
        var usuarioId = Guid.CreateVersion7();

        using (var scope = factory.Services.CreateScope())
        {
            var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();

            var rolId = Guid.CreateVersion7();
            identidad.Roles.Add(new Rol(rolId, $"test-autz-{random}", "Rol de prueba de autorización"));
            foreach (var codigo in permisoCodigos)
            {
                var permisoId = await identidad.Permisos.AsNoTracking()
                    .Where(p => p.Codigo == codigo).Select(p => p.Id).SingleAsync();
                identidad.RolPermisos.Add(new RolPermiso(Guid.CreateVersion7(), rolId, permisoId));
            }

            identidad.Usuarios.Add(new Usuario(usuarioId, oid, email, "Usuario Autorización"));
            identidad.UsuarioPreferencias.Add(new UsuarioPreferencia(Guid.CreateVersion7(), usuarioId));
            identidad.UsuarioEmpresaRoles.Add(new UsuarioEmpresaRol(
                Guid.CreateVersion7(), usuarioId, EmpresaInicialId, rolId, asignadoPorUsuarioId: null));
            await identidad.SaveChangesAsync();
        }

        var client = factory.CreateClientWithIdempotency();
        var token = await FakeLoginAsync(client, oid, email, "Usuario Autorización");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, usuarioId);
    }

    internal static async Task AsociarSucursalAsync(
        WebApplicationFactory<Program> factory, Guid usuarioId, Guid sucursalId)
    {
        using var scope = factory.Services.CreateScope();
        var identidad = scope.ServiceProvider.GetRequiredService<IdentidadDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        identidad.UsuarioSucursales.Add(new UsuarioSucursal(
            Guid.CreateVersion7(), usuarioId, sucursalId, EmpresaInicialId));
        await identidad.SaveChangesAsync();
    }

    /// <summary>
    /// Crea una RQ (borrador) con las líneas indicadas. Si se pasa
    /// <paramref name="sucursalId"/> debe pasarse también su código
    /// (<paramref name="sucursalCodigo"/>, p. ej. "MTY").
    /// </summary>
    internal static async Task<Guid> CrearRqAsync(
        HttpClient client,
        IReadOnlyList<(Guid ArticuloId, decimal Cantidad)> lineas,
        Guid? sucursalId = null,
        string? sucursalCodigo = null)
    {
        var body = TestComprasFixtures.BuildCrearRqValidBody(
            sucursalId: sucursalId,
            sucursalCodigo: sucursalCodigo,
            descripcion: "Test integration autorización");
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones", body);
        crear.EnsureSuccessStatusCode();
        var rqId = (await ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        foreach (var (articuloId, cantidad) in lineas)
        {
            var lineaBody = new
            {
                ArticuloId = articuloId,
                Cantidad = cantidad,
                UnidadMedida = "PZA",
                PrecioEstimadoMonto = 15m,
                PrecioEstimadoMoneda = "MXN",
                CuentaContableId = (Guid?)null,
                CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
                Proyecto = (string?)null,
                FechaRequerida = (DateOnly?)null,
                Notas = (string?)null,
            };
            var linea = await client.PostAsJsonAsync(
                $"/api/v1/compras/requisiciones/{rqId}/lineas", lineaBody);
            linea.EnsureSuccessStatusCode();
        }

        return rqId;
    }

    internal static async Task<Guid> CrearTransmitirAsync(
        HttpClient client,
        IReadOnlyList<(Guid ArticuloId, decimal Cantidad)> lineas,
        Guid? sucursalId = null,
        string? sucursalCodigo = null)
    {
        var rqId = await CrearRqAsync(client, lineas, sucursalId, sucursalCodigo);
        var transmit = await client.PostAsync(
            $"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmit.EnsureSuccessStatusCode();
        return rqId;
    }

    /// <summary>Estado numérico (<see cref="EstadoRequisicion"/>) de una RQ.</summary>
    internal static async Task<int> EstadoAsync(HttpClient client, Guid rqId)
    {
        var resp = await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}");
        resp.EnsureSuccessStatusCode();
        return (await ReadJsonAsync(resp)).GetProperty("estado").GetInt32();
    }

    /// <summary>POST /autorizaciones (firma de un nivel sobre una RQ).</summary>
    internal static Task<HttpResponseMessage> AutorizarAsync(HttpClient client, Guid rqId, int nivel) =>
        client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = nivel, Notas = (string?)null });

    internal static async Task<string> FakeLoginAsync(
        HttpClient client, string oid, string email, string nombre)
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

    internal static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStreamAsync();
        var doc = await JsonDocument.ParseAsync(json);
        return doc.RootElement.Clone();
    }
}
