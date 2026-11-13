using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Tambores;
using Millet.Api.Auth;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen;

/// <summary>
/// M-172: líneas de producción con sus tambores (activo e historial). Mismo permiso de lectura que la bandeja de
/// salidas; el alcance por sucursal lo aplica <c>SucursalScopeQueryBehavior</c> (ADR-0051).
/// </summary>
public static class TamboresLineaEndpoints
{
    public static IEndpointRouteBuilder MapTamboresLineaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/almacen/tambores-linea", async ([FromQuery] Guid? sucursalId, IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new ListarTamboresLineaQuery(sucursalId), ct)))
            .WithTags("Almacen")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
            .WithName("ListarTamboresLinea")
            .WithSummary("Líneas de producción con su tambor activo e historial")
            .Produces<IReadOnlyList<LineaTamboresDto>>(StatusCodes.Status200OK);
        return app;
    }
}
