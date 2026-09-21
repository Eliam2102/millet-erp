using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.IntegrationTests.Administracion;

public sealed class OrganizacionAdminAuthMetadataTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OrganizacionAdminAuthMetadataTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/admin/departamentos/", "GET", "admin.departamentos.leer")]
    [InlineData("/api/v1/admin/departamentos/", "POST", "admin.departamentos.gestionar")]
    [InlineData("/api/v1/admin/puestos/", "GET", "admin.puestos.leer")]
    [InlineData("/api/v1/admin/puestos/", "POST", "admin.puestos.gestionar")]
    public void Organizacion_Admin_Exige_Permiso_Por_Operacion(
        string route,
        string method,
        string permission)
    {
        _ = _factory.CreateClient();
        var endpoint = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route
                && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .Should()
            .Contain(PermissionPolicyProvider.Prefix + permission);
    }
}
