using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.SucursalPuestos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de asignaciones Sucursal ↔ Puesto ↔
/// Departamento (F1-ADM-01 Fase 2, reabierta 2026-09-24: un mismo puesto
/// puede asignarse a varios departamentos de la sucursal). Análogo de
/// <see cref="SucursalDepartamentosEndpoints"/> pero para puestos.
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/admin/empresas/sucursales/{sucursalId}/puestos</c>
///         — lista las asignaciones (una fila por puesto+departamento)
///         con su estatus en la sucursal. Filtro opcional
///         <c>departamentoId</c>.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}</c>
///         — asigna el puesto a un departamento (Idempotency-Key). Body
///         <c>{ departamentoId, rolSugeridoId? }</c>.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{departamentoId}/desactivar</c>
///         — desactiva esa asignación puntual.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{departamentoId}/reactivar</c>
///         — reactiva esa asignación puntual.</item>
///   <item><c>PATCH  /api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{departamentoId}</c>
///         — fija o limpia el rol sugerido de esa asignación. Body
///         <c>{ rolSugeridoId: guid|null }</c>.</item>
/// </list>
///
/// <para>Permisos (split por-ruta):
/// <list type="bullet">
///   <item>GET: <c>compartido.catalogos.leer</c> — mismo permiso que la
///         hermana de departamentos.</item>
///   <item>POST asignar / desactivar / reactivar / PATCH rol sugerido
///         (mutación): <c>admin.sucursales.puestos-gestionar</c>.</item>
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
            [FromQuery] Guid? departamentoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarPuestosDeSucursalQuery(sucursalId, departamentoId), ct);
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
            AsignarPuestoASucursalRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsignarPuestoASucursalCommand(
                    sucursalId, puestoId, request.DepartamentoId, request.RolSugeridoId), ct);
            return Results.Created(
                $"/api/v1/admin/empresas/sucursales/{sucursalId}/puestos/{puestoId}/departamentos/{request.DepartamentoId}",
                response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AsignarPuestoASucursal")
        .WithSummary("Asigna un puesto a un departamento de la sucursal (Activa)")
        .Produces<SucursalPuestoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{puestoId:guid}/departamentos/{departamentoId:guid}/desactivar", async (
            Guid sucursalId,
            Guid puestoId,
            Guid departamentoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new DesactivarAsignacionSucursalPuestoCommand(sucursalId, puestoId, departamentoId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarAsignacionSucursalPuesto")
        .WithSummary("Desactiva la asignación de un puesto a un departamento de la sucursal")
        .Produces<SucursalPuestoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{puestoId:guid}/departamentos/{departamentoId:guid}/reactivar", async (
            Guid sucursalId,
            Guid puestoId,
            Guid departamentoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ReactivarAsignacionSucursalPuestoCommand(sucursalId, puestoId, departamentoId), ct);
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

        group.MapPatch("/{puestoId:guid}/departamentos/{departamentoId:guid}", async (
            Guid sucursalId,
            Guid puestoId,
            Guid departamentoId,
            ActualizarRolSugeridoAsignacionSucursalPuestoRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarRolSugeridoAsignacionSucursalPuestoCommand(
                    sucursalId, puestoId, departamentoId, request.RolSugeridoId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesPuestosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarRolSugeridoAsignacionSucursalPuesto")
        .WithSummary("Fija o limpia el rol sugerido de la asignación puntual")
        .Produces<SucursalPuestoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}

public sealed record AsignarPuestoASucursalRequest(Guid DepartamentoId, Guid? RolSugeridoId = null);

public sealed record ActualizarRolSugeridoAsignacionSucursalPuestoRequest(Guid? RolSugeridoId);
