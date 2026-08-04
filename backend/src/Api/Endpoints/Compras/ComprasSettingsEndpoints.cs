using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compras.Application.Settings;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Endpoints HTTP para la configuración del módulo Compras por empresa.
/// La fila vive en <c>compras.settings</c>, una por empresa.
///
/// <list type="bullet">
///   <item><c>GET /api/v1/compras/configuracion</c> — devuelve la config
///         de la empresa actual del JWT. Permiso:
///         <c>compras.configuracion.leer</c>. Si no existe la fila,
///         devuelve la versión default sin persistir.</item>
///   <item><c>PATCH /api/v1/compras/configuracion</c> — upsert parcial.
///         Permiso: <c>compras.configuracion.editar</c>. Idempotency-Key
///         requerido (ADR-0020). Campos <c>null</c> = no tocar.</item>
/// </list>
///
/// <para>
/// El payload también viaja en <c>GET /api/auth/me</c> bajo el campo
/// <c>comprasSettings</c> para que el FE no tenga que hacer un fetch
/// extra al inicio de la sesión.
/// </para>
/// </summary>
public static class ComprasSettingsEndpoints
{
    public static IEndpointRouteBuilder MapComprasSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/compras/configuracion")
            .WithTags("Compras");

        group.MapGet("/", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerComprasSettingsQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasConfiguracionLeer)
        .WithName("GetComprasConfiguracion")
        .WithSummary("Leer configuración del módulo Compras de la empresa actual")
        .Produces<ComprasSettingsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/", async (
            [FromBody] ActualizarComprasSettingsCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasConfiguracionEditar)
        .WithName("PatchComprasConfiguracion")
        .WithSummary("Actualizar (upsert) configuración del módulo Compras")
        .WithDescription(
            "PATCH parcial: campos null = no tocar. Crea la fila si no existe " +
            "(default cuando la empresa no la ha personalizado).")
        .Produces<ComprasSettingsResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
