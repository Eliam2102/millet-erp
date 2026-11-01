using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.FoliosTransferencia;
using Millet.Tesoreria.Domain.FoliosTransferencia;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Folios de confirmación de transferencia (F6.6, R16): Ingresos los emite
/// desde un ingreso de cliente ya registrado; Caja solo aplica un cobro con
/// forma 03 si trae uno vigente, del cliente y por el importe exacto.
/// </summary>
public static class FoliosTransferenciaEndpoints
{
    public sealed record EmitirFolioBody(Guid MovimientoId, Guid ClienteId, decimal Importe, string? Pedido);
    public sealed record CancelarFolioBody(string Motivo);

    public static IEndpointRouteBuilder MapTesoreriaFoliosTransferenciaEndpoints(this IEndpointRouteBuilder app)
    {
        var folios = app
            .MapGroup("/api/v1/tesoreria/folios-transferencia")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        folios.MapGet("/", async (
            [FromQuery] EstadoFolioTransferencia? estado,
            [FromQuery] Guid? clienteId,
            [FromQuery] Guid? movimientoId,
            [FromQuery] string? folio,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new FoliosTransferenciaQuery(estado, clienteId, movimientoId, folio, offset ?? 0, limit ?? 50), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer)
        .WithName("ListarFoliosTransferencia")
        .Produces<PagedResponse<FolioTransferenciaResponse>>(StatusCodes.Status200OK);

        folios.MapGet("/conteos-por-estado", async ([FromQuery] string? folio, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConteosFoliosTransferenciaPorEstadoQuery(folio), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer)
        .WithName("ConteosFoliosTransferenciaPorEstado")
        .Produces<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>(StatusCodes.Status200OK);

        folios.MapPost("/", async ([FromBody] EmitirFolioBody body, IMediator mediator, CancellationToken ct) =>
        {
            var folio = await mediator.Send(
                new EmitirFolioTransferenciaCommand(body.MovimientoId, body.ClienteId, body.Importe, body.Pedido), ct);
            return Results.Created($"/api/v1/tesoreria/folios-transferencia?folio={folio.Folio}", folio);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaFoliosTransferenciaEmitir)
        .WithName("EmitirFolioTransferencia")
        .Produces<FolioTransferenciaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        folios.MapPost("/{id:guid}/cancelar", async (
            Guid id, [FromBody] CancelarFolioBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CancelarFolioTransferenciaCommand(id, body.Motivo), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaFoliosTransferenciaCancelar)
        .WithName("CancelarFolioTransferencia")
        .Produces<FolioTransferenciaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
