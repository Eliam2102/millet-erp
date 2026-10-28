using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth;

namespace Millet.Api.IntegrationTests.Administracion;

/// <summary>
/// Barrido de metadata de las rutas del flujo compra-a-pago (Compras, recepciones de Almacén,
/// Cuentas por pagar y Tesorería): ninguna es anónima y todas exigen una política de permiso.
/// Si una ruta nueva se registra sin <c>RequireAuthorization(permiso)</c>, esta prueba la delata.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class CompraAPagoAuthMetadataTests
{
    private static readonly string[] Prefijos =
    [
        "/api/v1/compras/",
        "/api/v1/almacen/recepciones",
        "/api/v1/cuentas-por-pagar/",
        "/api/v1/tesoreria/",
    ];

    // El permiso de estas rutas depende del cuerpo (Nivel) o del estado del documento y se valida dentro
    // del handler (403 AUTORIZAR_NIVEL_DENEGADO / OC_CANCELAR_DOBLE_DENEGADO), con RequireAuthorization() a secas
    // en la ruta. Lo cubre P6SucursalEndpointsTests.Permiso_del_paso_se_valida_antes_de_buscar_el_documento_o_su_sucursal.
    private static readonly string[] PermisoDinamicoEnHandler =
    [
        "POST /api/v1/compras/ordenes/{id:guid}/autorizaciones",
        "POST /api/v1/compras/ordenes/autorizaciones-masivas",
        "POST /api/v1/compras/ordenes/{id:guid}/rechazar",
        "POST /api/v1/compras/ordenes/{id:guid}/cancelar-con-recepciones",
        "POST /api/v1/compras/requisiciones/{id:guid}/autorizaciones",
        "POST /api/v1/compras/requisiciones/autorizaciones-masivas",
    ];

    private readonly WebApplicationFactory<Program> _factory;

    public CompraAPagoAuthMetadataTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Rutas_Compra_A_Pago_Exigen_Autenticacion_Y_Politica_De_Permiso()
    {
        _ = _factory.CreateClient();
        var rutas = _factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => Prefijos.Any(p => e.RoutePattern.RawText!.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // Un barrido vacío pasaría en falso si cambian los prefijos.
        rutas.Should().HaveCountGreaterThan(100);

        var sinProteccion = rutas
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || !e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                    .Any(d => d.Policy?.StartsWith(PermissionPolicyProvider.Prefix, StringComparison.Ordinal) == true))
            .Select(e => $"{string.Join(",", e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {e.RoutePattern.RawText}")
            .Order()
            .ToList();

        var huecos = sinProteccion.Except(PermisoDinamicoEnHandler).ToList();
        huecos.Should().BeEmpty("estas rutas no exigen política de permiso: {0}", string.Join(" | ", huecos));
    }
}
