using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Identidad.Application.Permisos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Identidad;

/// <summary>
/// Endpoint HTTP read-only del catálogo canónico de permisos
/// (F-Admin-PR3.2). Lo consume la UI de matriz de permisos por rol y
/// los selectores de "asignar permisos".
///
/// <list type="bullet">
///   <item><c>GET /api/v1/identidad/permisos</c>
///         (<c>?agrupado=true</c> opcional) — permiso
///         <c>identidad.permisos.leer</c>.</item>
/// </list>
/// </summary>
public static class PermisosEndpoints
{
    public static IEndpointRouteBuilder MapPermisosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/identidad/permisos")
            .WithTags("Identidad");

        group.MapGet("/", async (
            [FromQuery] bool? agrupado,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarPermisosQuery(AgrupadoPorModulo: agrupado), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadPermisosLeer)
        .WithName("ListarPermisos")
        .WithSummary("Listar catálogo canónico de permisos del sistema")
        .Produces<ListarPermisosResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
