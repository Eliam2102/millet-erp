using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Api.Auth;
using Millet.Api.Endpoints.CuentasPorCobrar;
using Millet.Identidad.Domain;

namespace Millet.Api.UnitTests;

/// <summary>
/// B7 (prueba Sprints 3-4): quien autoriza el ajuste de cobro (Ingresos, solo <c>aplicacion-pago.confirmar</c>) debe poder ver la
/// bandeja y el detalle de la propuesta; antes ambos exigían <c>aplicacion-pago.proponer</c> y respondían 403.
/// </summary>
public sealed class PropuestasAplicacionPermisosTests
{
    [Theory]
    [InlineData("ListarPropuestasAplicacion")]
    [InlineData("ObtenerPropuestaAplicacion")]
    [InlineData("AutorizarAjustePropuestaAplicacion")]
    [InlineData("RechazarAjustePropuestaAplicacion")]
    [InlineData("RevertirAjustePropuestaAplicacion")]
    public void Ingresos_con_solo_confirmar_ve_la_propuesta_y_autoriza_su_ajuste(string endpoint) =>
        Assert.Contains(PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar, Codigos(endpoint));

    [Theory]
    [InlineData("ListarPropuestasAplicacion")]
    [InlineData("ObtenerPropuestaAplicacion")]
    [InlineData("CrearPropuestaAplicacion")]
    [InlineData("ListarDepositosPorIdentificar")]
    public void CxC_con_proponer_sigue_viendo_y_proponiendo(string endpoint) =>
        Assert.Contains(PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer, Codigos(endpoint));

    [Theory]
    [InlineData("CrearPropuestaAplicacion")]
    [InlineData("ListarDepositosPorIdentificar")]
    public void Proponer_e_identificar_depositos_siguen_siendo_solo_de_CxC(string endpoint) =>
        Assert.DoesNotContain(PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar, Codigos(endpoint));

    /// <summary>Permisos que acepta la política de la ruta (<c>permiso:a|b</c> pasa con cualquiera).</summary>
    private static IReadOnlyList<string> Codigos(string nombre)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        using var provider = services.BuildServiceProvider();
        var rutas = new Rutas(new ConServicios(provider));
        rutas.MapPropuestasAplicacionEndpoints();
        var endpoint = rutas.DataSources.SelectMany(d => d.Endpoints)
            .Single(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == nombre);
        var politica = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(d => d.Policy).Single(p => p?.StartsWith(PermissionPolicyProvider.Prefix, StringComparison.Ordinal) == true)!;
        return new PermissionRequirement(politica[PermissionPolicyProvider.Prefix.Length..]).Codes;
    }

    /// <summary>
    /// Solo se lee la metadata de las rutas: los parámetros de servicio de los handlers (IMediator, DbContext, puertos) no están
    /// registrados, así que se declara que todo tipo complejo es servicio para que no se infiera como cuerpo.
    /// </summary>
    private sealed class ConServicios(IServiceProvider interno) : IServiceProvider, IServiceProviderIsService
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceProviderIsService) ? this : interno.GetService(serviceType);

        public bool IsService(Type serviceType) =>
            !(serviceType.IsPrimitive || serviceType.IsEnum || serviceType == typeof(string) || serviceType == typeof(Guid)
              || serviceType == typeof(decimal) || Nullable.GetUnderlyingType(serviceType) is not null);
    }

    private sealed class Rutas(IServiceProvider servicios) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider => servicios;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(servicios);
    }
}
