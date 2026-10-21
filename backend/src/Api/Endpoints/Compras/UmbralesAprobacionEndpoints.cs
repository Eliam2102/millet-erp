using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compras.Application.Umbrales;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Endpoints HTTP para los umbrales de aprobación N1/N2 por departamento
/// (motor de autorización §3.bis.2).
///
/// <list type="bullet">
///   <item><c>GET /api/v1/compras/umbrales-aprobacion</c> — umbrales
///         vigentes de la empresa actual. Permiso:
///         <c>compras.configuracion.leer</c>.</item>
///   <item><c>PUT /api/v1/compras/umbrales-aprobacion</c> — crea o
///         versiona el umbral de un departamento. Permiso:
///         <c>compras.configuracion.editar</c>. Idempotency-Key requerido
///         (ADR-0020).</item>
/// </list>
/// </summary>
public static class UmbralesAprobacionEndpoints
{
    public static IEndpointRouteBuilder MapUmbralesAprobacionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/compras/umbrales-aprobacion")
            .WithTags("Compras");

        group.MapGet("/", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ListarUmbralesQuery(), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasConfiguracionLeer)
        .WithName("ListarUmbralesAprobacion")
        .WithSummary("Listar umbrales N1/N2 vigentes por departamento")
        .Produces<IReadOnlyList<UmbralAprobacionResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/", async (
            [FromBody] UpsertUmbralCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasConfiguracionEditar)
        .WithName("PutUmbralAprobacion")
        .WithSummary("Crear o versionar el umbral N1/N2 de un departamento")
        .WithDescription(
            "Si la vigencia anterior es de un día previo, la cierra con hoy-1 e inserta " +
            "una nueva con vigenteDesde = hoy. Si ya existe una fila de hoy, la actualiza. " +
            "umbralMenorCuantia (N1) debe ser menor que umbralMonto (N2) cuando se envía. " +
            "modoNivel2 (SoloMonto, SoloDisponibilidad o Ambos; default SoloDisponibilidad) y montoMinimoDisponibilidad " +
            "(opcional, >= 0) definen cuándo una requisición del departamento exige N2. " +
            "Responde 409 UMBRAL_GUARDADO_CONCURRENTE si otro usuario guarda el mismo departamento al mismo tiempo.")
        .Produces<UmbralAprobacionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}
