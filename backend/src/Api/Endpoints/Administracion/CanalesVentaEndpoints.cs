using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.CanalesVenta;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Catalogos.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de Canales de Venta (FAC-ING-PR2). Catálogo
/// administrable en <c>compartido.canales_venta</c> — reemplaza el enum
/// hardcodeado de Facturación. Mismo patrón y permiso que Sucursales
/// (<c>admin.empresas.sucursales-gestionar</c>).
///
/// <list type="bullet">
///   <item><c>GET   /api/v1/admin/canales-venta</c> — lista completa
///         (filtro opcional <c>estatus</c>).</item>
///   <item><c>POST  /api/v1/admin/canales-venta</c> — alta (id = max+1
///         asignado por el handler; Idempotency-Key).</item>
///   <item><c>PATCH /api/v1/admin/canales-venta/{id}</c> — patch parcial
///         de nombre / clave A+W / estatus.</item>
/// </list>
/// </summary>
public static class CanalesVentaEndpoints
{
    public static IEndpointRouteBuilder MapCanalesVentaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/canales-venta")
            .WithTags("Administracion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasSucursalesGestionar);

        group.MapGet("/", async (
            [FromQuery] EstatusCatalogo? estatus,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ListarCanalesVentaQuery(estatus), ct);
            return Results.Ok(response);
        })
        .WithName("ListarCanalesVenta")
        .WithSummary("Listar canales de venta (catálogo administrable)")
        .Produces<IReadOnlyList<CanalVentaResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/", async (
            [FromBody] CrearCanalVentaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/admin/canales-venta/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearCanalVenta")
        .WithSummary("Crear canal de venta (id = max+1, asignado por la app)")
        .Produces<CanalVentaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:int}", async (
            short id,
            [FromBody] ActualizarCanalVentaPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarCanalVentaCommand(
                    id, payload.Nombre, payload.ClaveAw,
                    payload.LimpiarClaveAw ?? false, payload.Estatus), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarCanalVenta")
        .WithSummary("PATCH parcial sobre canal de venta (nombre / clave A+W / estatus)")
        .Produces<CanalVentaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    /// <summary>Payload del PATCH /canales-venta/{id}. <c>ClaveAw</c> es el
    /// GRUPPE con el que A+W refiere el canal (ingesta de pedidos,
    /// FAC-ING-PR2); <c>LimpiarClaveAw=true</c> lo desasocia.
    /// <c>Estatus</c> activa/desactiva el canal sin tocar el histórico.</summary>
    public sealed record ActualizarCanalVentaPayload(
        string? Nombre,
        string? ClaveAw = null,
        bool? LimpiarClaveAw = null,
        EstatusCatalogo? Estatus = null);
}
