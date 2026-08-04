using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Cajas.Cobros;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de cobros de mostrador (CAJAS-PR4, 12-cajas.md §6/§10).
/// <c>/api/v1/facturacion/cobros</c>: registrar (a la sesión abierta del
/// cajero), listar por sesión y cancelar (supervisor).
/// </summary>
public static class CobrosEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionCobrosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/cobros")
            .WithTags("Facturacion");

        // POST: registra el cobro de un comprobante timbrado ([Decisión 12-E]).
        group.MapPost("/", async (
            RegistrarCobroMostradorCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/cobros/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaOperar)
        .WithName("RegistrarCobroMostrador")
        .WithSummary("Registra el cobro de un comprobante en la sesión abierta del cajero")
        .Produces<CobroMostradorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST: batch de liquidación de ruta — N cobros todo-o-nada a la
        // sesión abierta del cajero, Origen = LiquidacionRuta ([12-7]).
        group.MapPost("/liquidacion-ruta", async (
            LiquidarRutaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created("/api/v1/facturacion/cobros", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaOperar)
        .WithName("LiquidarRuta")
        .WithSummary("Registra en batch los cobros de una liquidación de ruta (todo-o-nada)")
        .Produces<LiquidacionRutaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: comprobantes timbrados sin cobro vigente en el alcance del
        // cajero — candidatos del cobro unitario y de la liquidación de ruta.
        group.MapGet("/cobrables", async (
            [FromQuery] string? search,
            [FromQuery] int? limite,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(
                new ListarComprobantesCobrablesQuery(search, limite ?? 50), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaOperar)
        .WithName("ListarComprobantesCobrables")
        .WithSummary("Comprobantes timbrados sin cobro vigente dentro del alcance del cajero")
        .Produces<IReadOnlyList<ComprobanteCobrableItem>>(StatusCodes.Status200OK);

        // GET: cobros de una sesión (panel "Mi caja" / revisión del corte).
        group.MapGet("/", async (
            [FromQuery] Guid sesionId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var items = await mediator.Send(new ListarCobrosMostradorQuery(sesionId), cancellationToken);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaOperar)
        .WithName("ListarCobrosMostrador")
        .WithSummary("Cobros registrados en una sesión de caja")
        .Produces<IReadOnlyList<CobroMostradorItem>>(StatusCodes.Status200OK);

        // POST: cancela un cobro — reversa en sesión abierta del ejecutor o
        // ajuste pendiente drenado en la próxima apertura ([Decisión 12-C]).
        group.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            CancelarCobroRequest body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new CancelarCobroMostradorCommand(id, body.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaSupervisar)
        .WithName("CancelarCobroMostrador")
        .WithSummary("Cancela un cobro de mostrador (reversa o ajuste pendiente)")
        .Produces<CobroMostradorResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Body de la cancelación (el id del cobro va en el path).</summary>
    public sealed record CancelarCobroRequest(string Motivo);
}
