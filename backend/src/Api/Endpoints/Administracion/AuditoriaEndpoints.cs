using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Auditoria;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoint consolidado de auditoría (F-Admin-PR7.2, cierra
/// <c>PLATFORM-TODO(&lt;AuditUI&gt;)</c>). Consulta el log
/// <c>core.audit_log</c> (ADR-0008) con filtros server-side.
///
/// <para>
/// <c>desde</c> y <c>hasta</c> son obligatorios (formato ISO 8601
/// <c>YYYY-MM-DD</c>) — sin rango, el endpoint rechaza con 400 para
/// evitar table scans. El rango máximo permitido es 90 días.
/// </para>
/// </summary>
public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapAuditoriaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/auditoria", async (
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] string? modulo,
            [FromQuery] string? recurso,
            [FromQuery] string? accion,
            [FromQuery] Guid? usuarioId,
            [FromQuery] Guid? empresaId,
            [FromQuery] Guid? sucursalId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            // Required params: si faltan, retornar 400 ProblemDetails antes
            // de mandar al mediator. El validator FluentValidation enforce
            // las reglas semánticas (rango > 90 días, etc.) y produce 422.
            var errors = new Dictionary<string, string[]>();
            if (desde is null) errors["desde"] = ["El parámetro 'desde' es requerido (formato yyyy-MM-dd)."];
            if (hasta is null) errors["hasta"] = ["El parámetro 'hasta' es requerido (formato yyyy-MM-dd)."];
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors, statusCode: StatusCodes.Status400BadRequest);
            }

            var query = new ConsultarBitacoraQuery(
                Desde: desde!.Value,
                Hasta: hasta!.Value,
                Modulo: modulo,
                Recurso: recurso,
                Accion: accion,
                UsuarioId: usuarioId,
                EmpresaId: empresaId,
                SucursalId: sucursalId,
                Offset: offset ?? 0,
                Limit: limit ?? 50);

            var response = await mediator.Send(query, cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminAuditoriaLeer)
        .WithTags("Administracion")
        .WithName("GetAuditoria")
        .WithSummary("Consultar la bitácora consolidada (rango obligatorio, max 90 días)")
        .Produces<ConsultarBitacoraResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
