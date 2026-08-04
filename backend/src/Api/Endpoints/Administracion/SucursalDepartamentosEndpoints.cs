using MediatR;
using Microsoft.AspNetCore.Http;
using Millet.Administracion.Application.SucursalDepartamentos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de asignaciones Sucursal ↔ Departamento
/// (PR-A1). Modelan la N:M entre catálogos: un departamento opera (o no)
/// en una sucursal dada.
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/admin/empresas/sucursales/{sucursalId}/departamentos</c>
///         — lista los departamentos asignados con su estatus en la
///         sucursal.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/departamentos/{departamentoId}</c>
///         — asigna el departamento (Idempotency-Key).</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/departamentos/{departamentoId}/desactivar</c>
///         — desactiva la asignación.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{sucursalId}/departamentos/{departamentoId}/reactivar</c>
///         — reactiva la asignación.</item>
/// </list>
///
/// <para>Permisos (split por-ruta):
/// <list type="bullet">
///   <item>GET (lectura del N:M, alimenta el combo de captura de RQ y el
///         Sheet admin): <c>compartido.catalogos.leer</c> — mismo permiso
///         que los catálogos organizacionales hermanos (sucursales,
///         almacenes).</item>
///   <item>POST asignar / desactivar / reactivar (mutación):
///         <c>admin.sucursales.departamentos-gestionar</c>.</item>
/// </list>
/// </para>
/// </summary>
public static class SucursalDepartamentosEndpoints
{
    public static IEndpointRouteBuilder MapSucursalDepartamentosEndpoints(this IEndpointRouteBuilder app)
    {
        // Auth por-ruta (no a nivel de grupo): el GET es lectura de catálogo
        // y los POST son mutación. Ver doc del class para el detalle.
        var group = app
            .MapGroup("/api/v1/admin/empresas/sucursales/{sucursalId:guid}/departamentos")
            .WithTags("Administracion");

        group.MapGet("/", async (
            Guid sucursalId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarDepartamentosDeSucursalQuery(sucursalId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.CompartidoCatalogosLeer)
        .WithName("ListarDepartamentosDeSucursal")
        .WithSummary("Listar departamentos asignados a la sucursal")
        .Produces<ListarDepartamentosDeSucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{departamentoId:guid}", async (
            Guid sucursalId,
            Guid departamentoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new AsignarDepartamentoASucursalCommand(sucursalId, departamentoId), ct);
            return Results.Created(
                $"/api/v1/admin/empresas/sucursales/{sucursalId}/departamentos/{departamentoId}",
                response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesDepartamentosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AsignarDepartamentoASucursal")
        .WithSummary("Asigna un departamento a una sucursal (Activa)")
        .Produces<SucursalDepartamentoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{departamentoId:guid}/desactivar", async (
            Guid sucursalId,
            Guid departamentoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new DesactivarAsignacionSucursalDepartamentoCommand(sucursalId, departamentoId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesDepartamentosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarAsignacionSucursalDepartamento")
        .WithSummary("Desactiva la asignación (bloquea nuevas RQs; no afecta existentes)")
        .Produces<SucursalDepartamentoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{departamentoId:guid}/reactivar", async (
            Guid sucursalId,
            Guid departamentoId,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ReactivarAsignacionSucursalDepartamentoCommand(sucursalId, departamentoId), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(
            PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminSucursalesDepartamentosGestionar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReactivarAsignacionSucursalDepartamento")
        .WithSummary("Reactiva una asignación previamente desactivada")
        .Produces<SucursalDepartamentoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
