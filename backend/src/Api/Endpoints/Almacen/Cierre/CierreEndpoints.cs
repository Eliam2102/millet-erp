using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Almacen.Application.Cierre;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Cierre;

/// <summary>
/// Endpoints de cierre de mes (F8-PR2).
/// </summary>
public static class CierreEndpoints
{
    public static IEndpointRouteBuilder MapCierreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/almacen/cierre-mes", async (
            EjecutarCierreMensualCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCierreMesEjecutar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("EjecutarCierreMes")
        .WithSummary("Cierra el periodo mensual (Jefe Almacén). Valida que no haya conteos pendientes ni movimientos Borrador/Validado del mes.")
        .Produces<EjecutarCierreMensualResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
