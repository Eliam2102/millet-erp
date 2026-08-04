using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.Integraciones.Fiscal.Application.CatalogosSat;
using Millet.Integraciones.Fiscal.Domain.Ports;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Búsqueda typeahead de catálogos SAT en vivo vía FiscalAPI
/// (FAC-DET-PR1). A diferencia de <see cref="CatalogosSatEndpoints"/>
/// (catálogos chicos seedeados localmente), estos catálogos se
/// consultan al PAC para estar siempre en la versión vigente del SAT
/// (c_ClaveProdServ tiene ~50k claves — no se seedea).
///
/// <list type="bullet">
///   <item><c>GET /api/v1/catalogos/sat/clave-prod-serv?buscar=vidrio</c></item>
///   <item><c>GET /api/v1/catalogos/sat/clave-unidad?buscar=metro</c> (o código exacto: <c>?buscar=MTK</c>)</item>
///   <item><c>GET /api/v1/catalogos/sat/objeto-imp</c> (lista completa; <c>buscar</c> opcional)</item>
/// </list>
///
/// <para>
/// 503 <c>CATALOGO_SAT_NO_DISPONIBLE</c> cuando el SDK está apagado, la
/// empresa no tiene ConfiguracionPac o FiscalAPI falla — el frontend
/// degrada a captura manual. Permiso: <c>compartido.catalogos.leer</c>
/// (mismo que el resto de catálogos SAT).
/// </para>
/// </summary>
public static class CatalogosSatFiscalApiEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosSatFiscalApiEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/sat").WithTags("Catalogos");

        MapCatalogo(group, "clave-prod-serv", CatalogoSat.ClaveProdServ,
            "BuscarClaveProdServSat", "Búsqueda en c_ClaveProdServ vía FiscalAPI (FAC-DET-PR1)");
        MapCatalogo(group, "clave-unidad", CatalogoSat.ClaveUnidad,
            "BuscarClaveUnidadSat", "Búsqueda en c_ClaveUnidad vía FiscalAPI (FAC-DET-PR1)");
        MapCatalogo(group, "objeto-imp", CatalogoSat.ObjetoImp,
            "BuscarObjetoImpSat", "Búsqueda en c_ObjetoImp vía FiscalAPI (FAC-DET-PR1)");
        MapCatalogo(group, "fraccion-arancelaria", CatalogoSat.FraccionArancelaria,
            "BuscarFraccionArancelariaSat", "Búsqueda en c_FraccionArancelaria (CCE) vía FiscalAPI");
        MapCatalogo(group, "unidad-aduana", CatalogoSat.UnidadAduana,
            "BuscarUnidadAduanaSat", "Búsqueda en c_UnidadAduana (CCE) vía FiscalAPI");
        MapCatalogo(group, "pais", CatalogoSat.Pais,
            "BuscarPaisSat", "Búsqueda en c_Pais (CCE) vía FiscalAPI");
        MapCatalogo(group, "clave-pedimento", CatalogoSat.ClavePedimento,
            "BuscarClavePedimentoSat", "Búsqueda en c_ClavePedimento (CCE) vía FiscalAPI");

        return app;
    }

    private static void MapCatalogo(
        RouteGroupBuilder group, string ruta, CatalogoSat catalogo, string nombre, string summary)
    {
        group.MapGet($"/{ruta}", async (
            ISender sender, string? buscar, int? limit, CancellationToken ct) =>
        {
            var items = await sender.Send(
                new BuscarCatalogoSatQuery(catalogo, buscar ?? string.Empty, limit ?? 20), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName(nombre)
        .WithSummary(summary)
        .Produces<IReadOnlyList<CatalogoSatItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }
}
