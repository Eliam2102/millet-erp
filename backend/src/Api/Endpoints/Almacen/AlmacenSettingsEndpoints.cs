using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Settings;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// Endpoints HTTP para la configuración del módulo Almacén por empresa
/// (interruptor de reabasto automático). La fila vive en
/// <c>almacen.settings</c>, una por empresa. Molde 1:1 de
/// <c>ComprasSettingsEndpoints</c>.
///
/// <list type="bullet">
///   <item><c>GET /api/v1/almacen/configuracion</c> — config de la empresa
///         actual del JWT. Permiso: <c>almacen.reorden.leer</c> (el estado
///         del motor se muestra en la bandeja de Reabasto a quien la lee).
///         Si no existe la fila, devuelve el default sin persistir.</item>
///   <item><c>PATCH /api/v1/almacen/configuracion</c> — upsert parcial.
///         Permiso: <c>almacen.reorden.administrar</c> (encender/apagar el
///         motor es administrar el reorden). Idempotency-Key requerido
///         (ADR-0020). Campos <c>null</c> = no tocar.</item>
/// </list>
///
/// <para>
/// El <c>ReordenWorker</c> consume el flag por su propio camino (lectura
/// directa por la empresa del usuario de servicio, cada ciclo) — el efecto
/// de un cambio aquí puede tardar hasta el intervalo del worker.
/// </para>
/// </summary>
public static class AlmacenSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAlmacenSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/configuracion")
            .WithTags("Almacen");

        group.MapGet("/", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerAlmacenSettingsQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenRead)
        .WithName("GetAlmacenConfiguracion")
        .WithSummary("Leer configuración del módulo Almacén de la empresa actual")
        .Produces<AlmacenSettingsResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/", async (
            [FromBody] ActualizarAlmacenSettingsCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenReordenAdministrar)
        .WithName("PatchAlmacenConfiguracion")
        .WithSummary("Actualizar (upsert) configuración del módulo Almacén")
        .WithDescription(
            "PATCH parcial: campos null = no tocar. Crea la fila si no existe. " +
            "Encender/apagar el reabasto automático surte efecto en el siguiente " +
            "ciclo del ReordenWorker (no cancela el ciclo en curso).")
        .Produces<AlmacenSettingsResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
