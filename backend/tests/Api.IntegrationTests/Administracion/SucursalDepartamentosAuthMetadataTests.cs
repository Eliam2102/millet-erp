using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Red de seguridad RUTA-POR-RUTA del split de autorización del catálogo
/// Sucursal ↔ Departamento (D1). Inspecciona la metadata de los endpoints
/// (no el comportamiento HTTP) para asertar el permiso EXACTO de cada ruta:
///
/// <list type="bullet">
///   <item>GET <c>…/departamentos</c> → <c>compartido.catalogos.leer</c>.</item>
///   <item>Los 3 POST (asignar/desactivar/reactivar) → <c>admin.sucursales.departamentos-gestionar</c>.</item>
///   <item>Ninguna de las 4 rutas quedó anónima (sin autorización).</item>
/// </list>
///
/// <para>
/// Es más preciso que un test conductual: verifica el CÓDIGO de permiso por
/// ruta, no solo "hay auth". Si alguien revierte un <c>.RequireAuthorization</c>
/// del split, lo vuelve a nivel de grupo, o deja una ruta sin auth, este
/// test falla.
/// </para>
/// </summary>
public class SucursalDepartamentosAuthMetadataTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string LeerPolicy =
        PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer;
    private static readonly string GestionarPolicy =
        PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesDepartamentosGestionar;

    private readonly WebApplicationFactory<Program> _factory;

    public SucursalDepartamentosAuthMetadataTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Grupo_Tiene_4_Rutas_Y_Ninguna_Anonima()
    {
        var endpoints = GrupoEndpoints();

        endpoints.Should().HaveCount(4, "GET listar + 3 POST (asignar/desactivar/reactivar)");
        foreach (var e in endpoints)
        {
            e.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull(
                $"la ruta '{e.RoutePattern.RawText}' no debe ser anónima");
            e.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().NotBeEmpty(
                $"la ruta '{e.RoutePattern.RawText}' debe exigir autorización");
        }
    }

    [Fact]
    public void Get_Departamentos_Exige_CompartidoCatalogosLeer()
    {
        var get = GrupoEndpoints().Single(e => Metodo(e) == "GET");

        Politicas(get).Should().Contain(LeerPolicy);
        Politicas(get).Should().NotContain(GestionarPolicy,
            "el GET es lectura de catálogo, no gestión");
    }

    [Fact]
    public void Cada_Post_Exige_DepartamentosGestionar()
    {
        var posts = GrupoEndpoints().Where(e => Metodo(e) == "POST").ToList();

        posts.Should().HaveCount(3);
        foreach (var p in posts)
        {
            Politicas(p).Should().Contain(GestionarPolicy,
                $"la ruta de mutación '{p.RoutePattern.RawText}' debe exigir gestión");
            Politicas(p).Should().NotContain(LeerPolicy,
                $"la ruta '{p.RoutePattern.RawText}' es mutación, no debe bajar a lectura");
        }
    }

    // --- Helpers ---

    /// <summary>
    /// Las 4 rutas del grupo N:M: su patrón contiene tanto <c>sucursales</c>
    /// como <c>departamentos</c> (lo que excluye el catálogo global de
    /// departamentos y el CRUD de sucursales, que solo contienen uno) y NO
    /// contiene <c>puestos</c> — F1-ADM-01.4 reabierta agregó rutas
    /// <c>…/puestos/{puestoId}/departamentos/{departamentoId}/…</c> al
    /// grupo Sucursal↔Puesto↔Departamento que, sin este filtro, también
    /// matchean por contener ambas palabras.
    /// </summary>
    private List<RouteEndpoint> GrupoEndpoints()
    {
        // Forzar build/start del host para que el EndpointDataSource esté poblado.
        _ = _factory.CreateClient();
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();

        return source.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is string r
                && r.Contains("sucursales", StringComparison.OrdinalIgnoreCase)
                && r.Contains("departamentos", StringComparison.OrdinalIgnoreCase)
                && !r.Contains("puestos", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? Metodo(RouteEndpoint e)
    {
        var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        return methods is { Count: > 0 } ? methods[0] : null;
    }

    private static IEnumerable<string?> Politicas(RouteEndpoint e) =>
        e.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy);
}
