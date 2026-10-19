using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Revision;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

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

        group.MapPost("/lineas/{id:guid}/dictamen", async (
                Guid id, DictaminarMaterialEnRevisionRequest body, DocumentoSucursalScope documentos,
                IMediator mediator, CancellationToken ct) =>
            {
                var info = await mediator.Send(new ObtenerLineaRevisionQuery(id), ct)
                    ?? throw new EntityNotFoundException("REVISION_LINEA_NO_ENCONTRADA",
                        $"No existe una línea de material en revisión con id '{id}'.");
                await documentos.VerificarSucursalAsync(
                    info.SucursalId, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
                var response = await mediator.Send(new DictaminarMaterialEnRevisionCommand(
                    id, body.Resultado, body.Cantidad, body.Motivo, body.FechaMovimiento, body.UbicacionDestinoId), ct);
                return Results.Created($"/api/v1/almacen/revision/lineas/{id}", response);
            })
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCuarentenaDictaminar)
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("DictaminarMaterialEnRevision")
            .WithSummary("Dictamina material en revisión: liberar o dar de baja (ALM-04)")
            .Produces<DictamenRevisionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGroup("/lineas/{id:guid}").MapAdjuntos("linea_revision",
            PermisosCanonicos.AlmacenEntradasLeer, PermisosCanonicos.AlmacenEntradasCapturar,
            PermisosCanonicos.AlmacenEntradasCapturar);
        return app;
    }
}

public sealed record DictaminarMaterialEnRevisionRequest(
    ResultadoDictamen Resultado, decimal Cantidad, string Motivo, DateOnly FechaMovimiento, Guid? UbicacionDestinoId);
