using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Identidad;

/// <summary>
/// Consultas al directorio de Entra ID para el wizard de alta unificada
/// (plan 15, F2). Hoy responde el directorio simulado; el contrato se
/// mantiene cuando se conecte Graph.
/// </summary>
public static class DirectorioEntraEndpoints
{
    public static IEndpointRouteBuilder MapDirectorioEntraEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/identidad/directorio-entra").WithTags("Identidad");

        group.MapGet("/validar-correo", async (
            [FromQuery] string? correo,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ValidarCorreoCorporativoQuery(correo ?? string.Empty), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IdentidadUsuariosCrear)
        .WithName("ValidarCorreoCorporativo")
        .WithSummary("Validar un correo corporativo contra Entra ID")
        .WithDescription(
            "Indica si el dominio está permitido, si la cuenta ya existe en Entra " +
            "y si hay un usuario del ERP con ese correo. Lo usa el paso Acceso " +
            "del alta de colaborador. Permiso: `identidad.usuarios.crear`.")
        .Produces<ValidacionCorreoCorporativoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }
}
