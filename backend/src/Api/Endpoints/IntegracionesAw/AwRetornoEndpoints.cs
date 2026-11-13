using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.Integraciones.Aw.Infrastructure.Retorno;

namespace Millet.Api.Endpoints.IntegracionesAw;

/// <summary>M-160: avisos a A+W que esperan (tablero) y reproceso de la dead-letter del retorno.</summary>
public static class AwRetornoEndpoints
{
    public static IEndpointRouteBuilder MapAwRetornoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/integraciones/aw/retorno").WithTags("Integraciones A+W")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.IntegracionesAwAdministracionConfiguracion);

        group.MapGet("/pendientes/resumen", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ObtenerResumenRetornoPendienteQuery(), ct)))
            .Produces<AwRetornoPendienteResumen>(200).ProducesProblem(403);

        group.MapPost("/dead-letter/reprocesar", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new ReprocesarDeadLetterRetornoAwCommand(), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .Produces<ReprocesoDeadLetterRetornoAw>(200).ProducesProblem(403).ProducesProblem(422);
        return app;
    }
}
