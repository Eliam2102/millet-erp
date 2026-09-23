using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Administracion.Application.SucursalPuestos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de asignaciones Sucursal ↔ Puesto (F1-ADM-01
/// Fase 2). Análogo exacto de <see cref="SucursalDepartamentosEndpoints"/>
/// pero para puestos.
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/admin/empresas/sucursales/{sucursalId}/puestos</c>
///         — lista los puestos asignados con su estatus en la
///         sucursal.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}</c>
///         — asigna el puesto (Idempotency-Key).</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}/desactivar</c>
///         — desactiva la asignación.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}/reactivar</c>
///         — reactiva la asignación.</item>
/// </list>
///
/// <para>Permisos (split por-ruta):
/// <list type="bullet">
///   <item>GET: <c>compartido.catalogos.leer</c> — mismo permiso que la
///         hermana de departamentos.</item>
///   <item>POST asignar / desactivar / reactivar (mutación):
///         <c>admin.sucursales.puestos-gestionar</c>.</item>
/// </list>
/// </para>
/// </summary>
public static class SucursalPuestosEndpoints
{
    public static IEndpointRouteBuilder MapSucursalPuestosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/empresas/sucursales/{sucursalId:guid}/puestos")
            .WithTags("Administracion");

        group.MapGet("/", async (
            Guid sucursalId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarPuestosDeSucursalQuery(sucursalId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarPuestosDeSucursal")
        .WithSummary("Listar puestos asignados a la sucursal")
        .Produces<ListarPuestosDeSucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{puestoId:guid}", async (
            Guid sucursalId,
            Guid puestoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsignarPuestoASucursalCommand(sucursalId, puestoId), ct);
            return Results.Created(
                $"/api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}",
                response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AsignarPuestoASucursal")
        .WithSummary("Asigna un puesto a una sucursal (Activa)")
        .Produces<SucursalPuestoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{puestoId:guid}/desactivar", async (
            Guid sucursalId,
            Guid puestoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new DesactivarAsignacionSucursalPuestoCommand(sucursalId, puestoId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarAsignacionSucursalPuesto")
        .WithSummary("Desactiva la asignación")
        .Produces<SucursalPuestoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{puestoId:guid}/reactivar", async (
            Guid sucursalId,
            Guid puestoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ReactivarAsignacionSucursalPuestoCommand(sucursalId, puestoId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarAsignacionSucursalPuesto")
        .WithSummary("Reactiva una asignación previamente desactivada")
        .Produces<SucursalPuestoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
