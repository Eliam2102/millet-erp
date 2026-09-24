using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Application.UsuarioSucursales;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP de asignaciones Usuario ↔ Sucursal (F1-ADM-01 Fase 2).
/// Análogo de <see cref="SucursalDepartamentosEndpoints"/> pero para el
/// scoping de usuarios por sucursal (<c>UsuarioSucursal</c>, módulo
/// Identidad).
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/admin/empresas/sucursales/{sucursalId}/usuarios</c>
///         — lista los usuarios asignados con su estatus en la
///         sucursal.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/usuarios/{usuarioId}</c>
///         — asigna el usuario (Idempotency-Key). Valida que el usuario
///         tenga un rol activo en la empresa dueña de la sucursal
///         (409 <c>RELACION_INVALIDA</c> si no).</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/usuarios/{usuarioId}/desactivar</c>
///         — desasigna (desactiva) al usuario de la sucursal.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/usuarios/{usuarioId}/reactivar</c>
///         — reasigna (reactiva) al usuario a la sucursal.</item>
/// </list>
///
/// <para>Permisos (split por-ruta):
/// <list type="bullet">
///   <item>GET: <c>compartido.catalogos.leer</c>.</item>
///   <item>POST asignar / desactivar / reactivar (mutación):
///         <c>admin.sucursales.usuarios-gestionar</c>.</item>
/// </list>
/// </para>
/// </summary>
public static class SucursalUsuariosEndpoints
{
    public static IEndpointRouteBuilder MapSucursalUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/admin/usuarios/{usuarioId:guid}/sucursales", async (
            Guid usuarioId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarSucursalesDeUsuarioQuery(usuarioId), ct)))
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesUsuariosGestionar)
        .WithTags("Administracion")
        .WithName("ListarSucursalesDeUsuario")
        .WithSummary("Asignaciones de sucursal de un usuario en la empresa activa")
        .Produces<ListarUsuariosPorSucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        var group = app
            .MapGroup("/api/v1/admin/empresas/sucursales/{sucursalId:guid}/usuarios")
            .WithTags("Administracion");

        group.MapGet("/", async (
            Guid sucursalId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarUsuariosPorSucursalQuery(sucursalId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarUsuariosDeSucursal")
        .WithSummary("Listar usuarios asignados a la sucursal")
        .Produces<ListarUsuariosPorSucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{usuarioId:guid}", async (
            Guid sucursalId,
            Guid usuarioId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsignarUsuarioASucursalCommand(sucursalId, usuarioId), ct);
            return Results.Created(
                $"/api/v1/admin/empresas/sucursales/{sucursalId}/usuarios/{usuarioId}",
                response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesUsuariosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AsignarUsuarioASucursal")
        .WithSummary("Asigna un usuario a una sucursal (Activa)")
        .Produces<UsuarioSucursalResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{usuarioId:guid}/desactivar", async (
            Guid sucursalId,
            Guid usuarioId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new DesactivarAsignacionUsuarioSucursalCommand(sucursalId, usuarioId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesUsuariosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarAsignacionUsuarioSucursal")
        .WithSummary("Desasigna (desactiva) al usuario de la sucursal")
        .Produces<UsuarioSucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{usuarioId:guid}/reactivar", async (
            Guid sucursalId,
            Guid usuarioId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ReactivarAsignacionUsuarioSucursalCommand(sucursalId, usuarioId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesUsuariosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarAsignacionUsuarioSucursal")
        .WithSummary("Reasigna (reactiva) una asignación previamente desactivada")
        .Produces<UsuarioSucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
