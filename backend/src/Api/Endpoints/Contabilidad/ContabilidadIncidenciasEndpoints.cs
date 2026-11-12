using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Incidencias;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// M-321 (86e4084j7): incidencias posteriores bajo <c>/api/v1/contabilidad/incidencias-posteriores</c>. Consulta con el permiso de lectura
/// de pólizas; registrar y resolver con el de captura (no se crean permisos, igual que la revaluación y K10.7).
/// </summary>
public static class ContabilidadIncidenciasEndpoints
{
    public sealed record ResolverBody(Guid? PolizaAjusteId, Guid? ReaperturaId, string? Nota);

    public static IEndpointRouteBuilder MapContabilidadIncidenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var capturar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasCapturar;
        var g = app.MapGroup("/api/v1/contabilidad/incidencias-posteriores").WithTags("Contabilidad · Incidencias posteriores").RequireAuthorization();

        g.MapGet("/", async ([FromQuery] EstadoIncidenciaPosterior? estado, [FromQuery] int? anio, [FromQuery] int? numero, [FromQuery] Guid? polizaId,
                IMediator mediator, CancellationToken ct) => Results.Ok(await mediator.Send(new ListarIncidenciasPosterioresQuery(estado, anio, numero, polizaId), ct)))
            .RequireAuthorization(leer).WithName("ListarIncidenciasPosteriores")
            .WithSummary("Incidencias posteriores (por estado, periodo o póliza), la más reciente primero")
            .Produces<IReadOnlyList<IncidenciaPosteriorResponse>>();

        g.MapPost("/", async (RegistrarIncidenciaPosteriorCommand b, IMediator mediator, CancellationToken ct) =>
                Results.Created("/api/v1/contabilidad/incidencias-posteriores", await mediator.Send(b, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(capturar).WithName("RegistrarIncidenciaPosterior")
            .WithSummary("Registra una incidencia ligada a una póliza autorizada o a un periodo cerrado")
            .Produces<IncidenciaPosteriorResponse>(StatusCodes.Status201Created).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/{id:guid}/resolver", async (Guid id, [FromBody] ResolverBody b, IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ResolverIncidenciaPosteriorCommand(id, b.PolizaAjusteId, b.ReaperturaId, b.Nota), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(capturar).WithName("ResolverIncidenciaPosterior")
            .WithSummary("Resuelve la incidencia ligando su póliza de ajuste o la reapertura acotada del periodo")
            .Produces<IncidenciaPosteriorResponse>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return app;
    }
}
