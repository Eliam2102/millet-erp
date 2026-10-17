using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Revision;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Revision;

/// <summary>
/// ALM-04: bandeja «Material en revisión» y evidencia por línea (servicio genérico de adjuntos G1.2).
/// </summary>
public static class MaterialEnRevisionEndpoints
{
    public static IEndpointRouteBuilder MapMaterialEnRevisionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/almacen/revision").WithTags("Almacen").RequireAuthorization();

        group.MapGet("/", async ([FromQuery] bool? soloPendientes, [FromQuery] string? q, IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ListarMaterialEnRevisionQuery(soloPendientes ?? true, q), ct)))
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasLeer)
            .WithName("ListarMaterialEnRevision")
            .WithSummary("Bandeja de material en revisión (ALM-04)")
            .Produces<IReadOnlyList<MaterialEnRevisionItem>>(StatusCodes.Status200OK);

        group.MapGroup("/lineas/{id:guid}").MapAdjuntos("linea_revision",
            PermisosCanonicos.AlmacenEntradasLeer, PermisosCanonicos.AlmacenEntradasCapturar,
            PermisosCanonicos.AlmacenEntradasCapturar);
        return app;
    }
}
