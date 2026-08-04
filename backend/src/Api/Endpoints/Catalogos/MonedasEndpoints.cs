using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Application.Monedas;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Catalogos;

/// <summary>
/// Endpoints HTTP de catálogo de Monedas + TiposCambio (F-Admin-PR5.1).
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/catalogos/monedas</c> — lista (permiso
///         <c>compartido.catalogos.leer</c>).</item>
///   <item><c>POST   /api/v1/catalogos/monedas</c> — alta (permiso
///         <c>catalogos.monedas.gestionar</c>, Idempotency-Key).</item>
///   <item><c>PATCH  /api/v1/catalogos/monedas/{id}</c> — PATCH parcial.</item>
///   <item><c>GET    /api/v1/catalogos/monedas/{id}/tipos-cambio</c> —
///         histórico paginado por moneda.</item>
///   <item><c>POST   /api/v1/catalogos/monedas/{id}/tipos-cambio</c> —
///         registrar valor (permiso <c>catalogos.tipos-cambio.gestionar</c>).</item>
/// </list>
/// </summary>
public static class MonedasEndpoints
{
    public static IEndpointRouteBuilder MapMonedasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalogos/monedas").WithTags("Catalogos");

        // --- LIST ---
        group.MapGet("/", async (
            [FromQuery] bool? soloActivas,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ListarMonedasQuery(soloActivas), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarMonedas")
        .WithSummary("Listar monedas del catálogo cross-empresa")
        .Produces<IReadOnlyList<MonedaResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- CREATE ---
        group.MapPost("/", async (
            [FromBody] CrearMonedaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/catalogos/monedas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosMonedasGestionar)
        .WithName("CrearMoneda")
        .WithSummary("Crear moneda (F-Admin-PR5.1)")
        .Produces<MonedaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- PATCH ---
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarMonedaPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarMonedaCommand(id, payload.Nombre, payload.Decimales, payload.Activa),
                ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosMonedasGestionar)
        .WithName("ActualizarMoneda")
        .WithSummary("Editar moneda (PATCH parcial, F-Admin-PR5.1)")
        .Produces<MonedaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- TIPOS DE CAMBIO ---
        group.MapGet("/{id:guid}/tipos-cambio", async (
            Guid id,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarTiposCambioPorMonedaQuery(id, offset ?? 0, limit ?? 50), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarTiposCambioPorMoneda")
        .WithSummary("Histórico de tipos de cambio por moneda")
        .Produces<ListarTiposCambioResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/tipos-cambio", async (
            Guid id,
            [FromBody] RegistrarTipoCambioPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new RegistrarTipoCambioCommand(id, payload.Fecha, payload.ValorEnMxn, payload.Origen ?? OrigenTipoCambio.Manual),
                ct);
            return Results.Created(
                $"/api/v1/catalogos/monedas/{id}/tipos-cambio/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CatalogosTiposCambioGestionar)
        .WithName("RegistrarTipoCambio")
        .WithSummary("Registrar tipo de cambio (F-Admin-PR5.1)")
        .Produces<TipoCambioResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    public sealed record ActualizarMonedaPayload(string? Nombre, int? Decimales, bool? Activa);

    public sealed record RegistrarTipoCambioPayload(
        DateOnly Fecha,
        decimal ValorEnMxn,
        OrigenTipoCambio? Origen);
}
