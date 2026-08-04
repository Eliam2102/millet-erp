using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Almacen.Application.Conteos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Conteos;

/// <summary>
/// Endpoints del flujo de aprobación + aplicación de conteos (F7-PR2).
/// Bajo <c>/api/v1/almacen/conteos/{id}/...</c>. Permisos diferenciados
/// por monto (A8) cuando F9 introduzca la política completa; en F7-PR2
/// se exige <c>aprobar-nivel1</c> base y se documenta el incremento
/// por monto en docs/runbook.
/// </summary>
public static class AprobacionEndpoints
{
    public static IEndpointRouteBuilder MapAprobacionConteoEndpoints(this IEndpointRouteBuilder app)
    {
        // POST /{id}/lineas/{lineaId}/recuento
        app.MapPost("/api/v1/almacen/conteos/{id:guid}/lineas/{lineaId:guid}/recuento", async (
            Guid id, Guid lineaId,
            AgregarRecuentoRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AgregarRecuentoCommand(id, lineaId, req.CantidadRecontada), ct);
            return Results.Created(
                $"/api/v1/almacen/conteos/{id}/recuentos/{response.RecuentoId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCapturar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("AgregarRecuento")
        .WithSummary("Agregar recuento adicional a una línea (A7 — variación > umbral exige recuento)")
        .Produces<AgregarRecuentoResponse>(StatusCodes.Status201Created);

        // POST /{id}/evaluar-variaciones
        app.MapPost("/api/v1/almacen/conteos/{id:guid}/evaluar-variaciones", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var marcadas = await mediator.Send(new EvaluarVariacionesConteoCommand(id), ct);
            return Results.Ok(new { lineas_marcadas_para_recuento = marcadas });
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCapturar)
        .WithTags("Almacen")
        .WithName("EvaluarVariacionesConteo")
        .WithSummary("Aplica reglas A7 (variación > 5% o > $1K) y marca líneas que requieren recuento")
        .Produces(StatusCodes.Status200OK);

        // POST /{id}/lineas/{lineaId}/aprobar-individualmente
        app.MapPost("/api/v1/almacen/conteos/{id:guid}/lineas/{lineaId:guid}/aprobar-individualmente", async (
            Guid id, Guid lineaId,
            AprobarLineaRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new AprobarLineaIndividualmenteCommand(id, lineaId, req.Justificacion), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosAprobarNivel1)
        .WithTags("Almacen")
        .WithName("AprobarLineaIndividualmente")
        .WithSummary("Aprobar línea con variación bajo umbral o con justificación (A7)")
        .Produces(StatusCodes.Status204NoContent);

        // POST /{id}/aprobar (Aprobador firma el conteo)
        app.MapPost("/api/v1/almacen/conteos/{id:guid}/aprobar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new AprobarConteoCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosAprobarNivel1)
        .WithTags("Almacen")
        .WithName("AprobarConteo")
        .WithSummary("Aprobar conteo (A8 — política por monto). Requiere todas las líneas conflictivas resueltas (recuento o aprobación individual).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST /{id}/rechazar
        app.MapPost("/api/v1/almacen/conteos/{id:guid}/rechazar", async (
            Guid id,
            RechazarConteoRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new RechazarConteoCommand(id, req.Motivo), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosAprobarNivel1)
        .WithTags("Almacen")
        .WithName("RechazarConteo")
        .Produces(StatusCodes.Status204NoContent);

        // POST /{id}/aplicar — genera ajustes en batch.
        app.MapPost("/api/v1/almacen/conteos/{id:guid}/aplicar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var response = await mediator.Send(new AplicarConteoCommand(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosAprobarNivel1)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("AplicarConteo")
        .WithSummary("Aplica el conteo aprobado: genera movimientos AjustePositivo/AjusteNegativo en batch + publica AjusteInventarioAplicadoEvent")
        .Produces<AplicarConteoResponse>(StatusCodes.Status200OK);

        return app;
    }
}

public sealed record AgregarRecuentoRequest(decimal CantidadRecontada);
public sealed record AprobarLineaRequest(string Justificacion);
public sealed record RechazarConteoRequest(string Motivo);
