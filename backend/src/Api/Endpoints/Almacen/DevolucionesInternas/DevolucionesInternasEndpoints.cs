using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.DevolucionesInternas;

/// <summary>
/// Endpoints del sub-flujo 8.A (Devolución Interna) y MAT-REV
/// (F5-PR1).
/// </summary>
public static class DevolucionesInternasEndpoints
{
    public static IEndpointRouteBuilder MapDevolucionesInternasEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /api/v1/almacen/devoluciones-internas
        app.MapPost("/api/v1/almacen/devoluciones-internas", async (
            AplicarDevolucionInternaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/movimientos/{response.DevolucionId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasCapturar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("AplicarDevolucionInterna")
        .WithSummary("Aplicar devolución interna 8.A (restituye al costo de la salida origen)")
        .Produces<AplicarDevolucionInternaResponse>(StatusCodes.Status201Created);

        // POST /api/v1/almacen/mat-rev/baja-por-dano
        app.MapPost("/api/v1/almacen/mat-rev/baja-por-dano", async (
            BajaPorDanoCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/movimientos/{response.MovimientoId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasCapturar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("BajaPorDano")
        .WithSummary("Decisión Calidad: dar de baja material en MAT-REV (destrucción) (A15)")
        .Produces<BajaPorDanoResponse>(StatusCodes.Status201Created);

        // POST /api/v1/almacen/mat-rev/reincorporar
        app.MapPost("/api/v1/almacen/mat-rev/reincorporar", async (
            ReincorporacionTrasRevisionCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/movimientos/{response.MovimientoId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasCapturar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("ReincorporarTrasRevision")
        .WithSummary("Decisión Calidad: reincorporar material en MAT-REV al inventario activo (A15)")
        .Produces<BajaPorDanoResponse>(StatusCodes.Status201Created);

        return app;
    }
}
