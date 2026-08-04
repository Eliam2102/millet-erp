using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Catalogos.AprobadoresLimites;
using Millet.CuentasPorPagar.Application.Catalogos.PoliticasViaticos;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints CRUD para los catálogos locales de CxP (F7-PR3):
/// <list type="bullet">
///   <item><c>aprobadores_limites</c> — aprobadores con monto máximo por tipo de gasto.</item>
///   <item><c>politicas_viaticos</c> — tope por puesto + destino.</item>
/// </list>
/// </summary>
public static class CatalogosCxpAdminEndpoints
{
    public static IEndpointRouteBuilder MapCatalogosCxpAdminEndpoints(this IEndpointRouteBuilder app)
    {
        // -------- Aprobadores con límite --------
        var aprobadores = app
            .MapGroup("/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        aprobadores.MapGet("/", async (
            [FromQuery] Guid? empleadoId,
            [FromQuery] TipoGastoAprobador? tipoGasto,
            [FromQuery] bool? soloVigentes,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarAprobadoresLimitesQuery(
                    empleadoId, tipoGasto, soloVigentes ?? true, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar)
        .WithName("ListarAprobadoresLimites")
        .Produces<PagedResponse<AprobadorLimiteResponse>>(StatusCodes.Status200OK);

        aprobadores.MapPost("/", async (
            [FromBody] CrearAprobadorLimiteCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar)
        .WithName("CrearAprobadorLimite")
        .Produces<AprobadorLimiteResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        aprobadores.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ActualizarAprobadorBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ActualizarAprobadorLimiteCommand(id, v, body.MontoMax, body.Moneda), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar)
        .WithName("ActualizarAprobadorLimite")
        .Produces<AprobadorLimiteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        aprobadores.MapPost("/{id:guid}/cerrar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] CerrarAprobadorBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new CerrarAprobadorLimiteCommand(id, v, body.Fecha), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar)
        .WithName("CerrarAprobadorLimite")
        .Produces<AprobadorLimiteResponse>(StatusCodes.Status200OK);

        // -------- Políticas de viáticos --------
        var politicas = app
            .MapGroup("/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        politicas.MapGet("/", async (
            [FromQuery] Guid? puestoId,
            [FromQuery] TipoDestinoViatico? tipoDestino,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarPoliticasViaticosQuery(puestoId, tipoDestino, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar)
        .WithName("ListarPoliticasViaticos")
        .Produces<PagedResponse<PoliticaViaticosResponse>>(StatusCodes.Status200OK);

        politicas.MapPost("/", async (
            [FromBody] CrearPoliticaViaticosCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar)
        .WithName("CrearPoliticaViaticos")
        .Produces<PoliticaViaticosResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        politicas.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ActualizarPoliticaBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ActualizarPoliticaViaticosCommand(id, v, body.MontoMaxDia, body.DiasMax, body.Moneda),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar)
        .WithName("ActualizarPoliticaViaticos")
        .Produces<PoliticaViaticosResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        return app;
    }

    public sealed record ActualizarAprobadorBody(decimal MontoMax, string Moneda);
    public sealed record CerrarAprobadorBody(DateOnly Fecha);
    public sealed record ActualizarPoliticaBody(decimal MontoMaxDia, int DiasMax, string Moneda);
}
