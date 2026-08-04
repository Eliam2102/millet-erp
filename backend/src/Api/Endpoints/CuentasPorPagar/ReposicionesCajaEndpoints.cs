using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.ComprobacionGastos.Reposiciones;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints de reposiciones de caja chica (GI-PR1, doc 12 §D2/Q4):
/// bandeja de reposiciones emitidas, saldos acumulados por reponer,
/// corte manual y configuración del monto mínimo por sucursal.
/// </summary>
public static class ReposicionesCajaEndpoints
{
    public static IEndpointRouteBuilder MapReposicionesCajaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/reposiciones-caja")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] Guid? sucursalId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarReposicionesQuery(sucursalId, offset ?? 0, limit ?? 50), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReposicionesLeer)
        .WithName("ListarReposicionesCaja")
        .Produces<PagedResponse<ReposicionListItemResponse>>(StatusCodes.Status200OK);

        group.MapGet("/saldos", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ListarSaldosPendientesQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReposicionesLeer)
        .WithName("ListarSaldosReposicionPendientes")
        .WithSummary("Saldos acumulados por reponer por (sucursal, destino, moneda)")
        .Produces<IReadOnlyList<SaldoPendienteResponse>>(StatusCodes.Status200OK);

        group.MapPost("/emitir", async (
            [FromBody] EmitirReposicionManualCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReposicionesAdministrar)
        .WithName("EmitirReposicionManual")
        .WithSummary("Corte manual: emite la reposición del saldo acumulado sin esperar el mínimo")
        .Produces<EmitirReposicionManualResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPut("/configuracion", async (
            [FromBody] ConfigurarReposicionCajaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarReposicionesAdministrar)
        .WithName("ConfigurarReposicionCaja")
        .WithSummary("Configura (upsert) el monto mínimo de reposición de una sucursal")
        .Produces<ConfiguracionReposicionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        return app;
    }
}
