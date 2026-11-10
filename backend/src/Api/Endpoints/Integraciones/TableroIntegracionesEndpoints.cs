using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compartido.Application.TableroIntegraciones;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Integraciones;

/// <summary>
/// I13.6 · tablero de integraciones: estado de cada integración (eventos a Service
/// Bus, cola de errores, envíos a Almacenes, correo de CFDI), reintento manual y
/// alertas abiertas o recuperadas. Es de operación técnica: lo ve quien puede leer
/// la salud del sistema (<c>infra.health.leer</c>; supuesto a validar).
/// </summary>
public static class TableroIntegracionesEndpoints
{
    public static IEndpointRouteBuilder MapTableroIntegracionesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/integraciones/tablero")
            .WithTags("Integraciones")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.InfraHealthLeer);

        group.MapGet("/", async (TableroIntegracionesServicio tablero, CancellationToken ct) =>
            Results.Ok(await tablero.LeerAsync(ct)))
        .WithName("LeerTableroIntegraciones")
        .WithSummary("Estado de cada integración con sus mensajes más relevantes y las alertas abiertas")
        .Produces<TableroIntegracionesResponse>();

        group.MapGet("/alertas", async ([FromQuery] int? limit, TableroIntegracionesServicio tablero, CancellationToken ct) =>
            Results.Ok(await tablero.AlertasAsync(limit ?? 50, ct)))
        .WithName("ListarAlertasIntegracion")
        .WithSummary("Alertas de integración abiertas y recuperadas, de la más reciente a la más vieja")
        .Produces<IReadOnlyList<AlertaIntegracionResponse>>();

        group.MapPost("/{clave}/mensajes/{mensajeId}/reintentar", async (
            string clave, string mensajeId, TableroIntegracionesServicio tablero, CancellationToken ct) =>
        {
            await tablero.ReintentarAsync(clave, mensajeId, ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReintentarMensajeIntegracion")
        .WithSummary("Regresa un mensaje con error a la cola de su integración para que el worker lo vuelva a intentar")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
