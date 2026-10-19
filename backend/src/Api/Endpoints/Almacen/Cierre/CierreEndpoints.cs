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
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            // El cierre afecta a todas las sucursales: solo alcance corporativo.
            await documentos.VerificarSucursalAsync(null, PermisosCanonicos.AlmacenCierreMesGestionarTodasSucursales, ct);
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCierreMesEjecutar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithTags("Almacen")
        .WithName("EjecutarCierreMes")
        .WithSummary("Cierra el periodo mensual (Jefe Almacén). Valida que no haya conteos pendientes ni movimientos Borrador/Validado del mes.")
        .Produces<EjecutarCierreMensualResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/almacen/cierre-mes/periodos", async (
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarSucursalAsync(null, PermisosCanonicos.AlmacenCierreMesGestionarTodasSucursales, ct);
            return Results.Ok(await mediator.Send(new ListarPeriodosCierreQuery(), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCierreMesEjecutar)
        .WithTags("Almacen")
        .WithName("ListarPeriodosCierreMes")
        .WithSummary("Periodos mensuales concluidos con su estado (Cerrado/Abierto).")
        .Produces<IReadOnlyList<PeriodoCierreItem>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        app.MapGet("/api/v1/almacen/cierre-mes/revision-previa", async (
            int anio,
            int mes,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarSucursalAsync(null, PermisosCanonicos.AlmacenCierreMesGestionarTodasSucursales, ct);
            return Results.Ok(await mediator.Send(new ObtenerRevisionPreviaCierreQuery(anio, mes), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenCierreMesEjecutar)
        .WithTags("Almacen")
        .WithName("ObtenerRevisionPreviaCierreMes")
        .WithSummary("Revisión previa del cierre: mismas validaciones que el cierre, solo lectura.")
        .Produces<RevisionPreviaCierreResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
