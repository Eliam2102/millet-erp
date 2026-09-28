using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Administracion.Application.Empresas;
using Millet.Administracion.Application.Sucursales;
using Millet.Administracion.Domain;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Administracion;

/// <summary>
/// Endpoints HTTP del CRUD de Empresas + Sucursales (F-Admin-PR2.3).
///
/// <list type="bullet">
///   <item><c>GET    /api/v1/admin/empresas</c> — paginado (permiso
///         <c>admin.empresas.leer</c>).</item>
///   <item><c>POST   /api/v1/admin/empresas</c> — alta (permiso
///         <c>admin.empresas.crear</c>, Idempotency-Key).</item>
///   <item><c>GET    /api/v1/admin/empresas/{id}</c> — detalle con
///         sucursales y departamentos.</item>
///   <item><c>PATCH  /api/v1/admin/empresas/{id}</c> — PATCH parcial
///         (permiso <c>admin.empresas.editar</c>).</item>
///   <item><c>POST   /api/v1/admin/empresas/{id}/desactivar</c> — soft
///         delete (permiso <c>admin.empresas.desactivar</c>).</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales</c> — alta
///         sucursal (permiso <c>admin.empresas.sucursales-gestionar</c>).</item>
///   <item><c>PATCH  /api/v1/admin/empresas/sucursales/{id}</c> — patch
///         sucursal.</item>
///   <item><c>POST   /api/v1/admin/empresas/sucursales/{id}/desactivar</c>.</item>
/// </list>
/// </summary>
public static class EmpresasEndpoints
{
    public static IEndpointRouteBuilder MapEmpresasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/admin/empresas")
            .WithTags("Administracion");

        // --- LIST ---
        group.MapGet("/", async (
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] bool? soloActivas,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ListarEmpresasQuery(
                    Offset: offset ?? 0,
                    Limit: limit ?? 50,
                    SoloActivas: soloActivas),
                ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasLeer)
        .WithName("ListarEmpresas")
        .WithSummary("Listar empresas paginadas")
        .Produces<ListarEmpresasResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // --- CREATE ---
        group.MapPost("/", async (
            [FromBody] CrearEmpresaCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/admin/empresas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasCrear)
        .WithName("CrearEmpresa")
        .WithSummary("Crear empresa nueva")
        .Produces<EmpresaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // --- DETALLE ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new ObtenerEmpresaQuery(id), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasLeer)
        .WithName("ObtenerEmpresa")
        .WithSummary("Detalle de empresa con sucursales + departamentos")
        .Produces<EmpresaDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- PATCH ---
        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarEmpresaPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new ActualizarEmpresaCommand(
                id,
                payload.RazonSocial,
                payload.NombreComercial,
                payload.RegimenFiscal,
                payload.LimpiarNombreComercial ?? false,
                payload.TasaIvaDefault,
                payload.LimpiarTasaIvaDefault ?? false,
                payload.CodigoPostal);
            var response = await mediator.Send(command, ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasEditar)
        .WithName("ActualizarEmpresa")
        .WithSummary("PATCH parcial sobre empresa")
        .Produces<EmpresaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- DESACTIVAR ---
        group.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarEmpresaCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasDesactivar)
        .WithName("DesactivarEmpresa")
        .WithSummary("Desactivar empresa (valida: sin sucursales activas)")
        .Produces<EmpresaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- SUCURSALES (sub-rutas montadas bajo /empresas/) ---
        var sucursales = app
            .MapGroup("/api/v1/admin/empresas/sucursales")
            .WithTags("Administracion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AdminEmpresasSucursalesGestionar);

        sucursales.MapPost("/", async (
            [FromBody] CrearSucursalCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/admin/empresas/sucursales/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearSucursal")
        .WithSummary("Crear sucursal (catálogo organizacional)")
        .Produces<SucursalResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        sucursales.MapPatch("/{id:guid}", async (
            Guid id,
            [FromBody] ActualizarSucursalPayload payload,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(
                new ActualizarSucursalCommand(
                    id, payload.Nombre, payload.Tipo, payload.ClaveAw,
                    payload.LimpiarClaveAw ?? false,
                    payload.ZonaHoraria), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ActualizarSucursal")
        .WithSummary("PATCH parcial sobre sucursal")
        .Produces<SucursalResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        sucursales.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new DesactivarSucursalCommand(id), ct);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("DesactivarSucursal")
        .WithSummary("Desactivar sucursal")
        .Produces<SucursalResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Payload del PATCH /empresas/{id} — separado del command para
    /// que el caller no envíe el <c>Id</c> en el body (viene del path).</summary>
    public sealed record ActualizarEmpresaPayload(
        string? RazonSocial,
        string? NombreComercial,
        string? RegimenFiscal,
        bool? LimpiarNombreComercial,
        decimal? TasaIvaDefault = null,
        bool? LimpiarTasaIvaDefault = null,
        string? CodigoPostal = null);

    /// <summary>Payload del PATCH /sucursales/{id}. <c>ClaveAw</c> es la
    /// clave con la que A+W refiere la sucursal (ingesta de pedidos,
    /// ADR-0048); <c>LimpiarClaveAw=true</c> la desasocia.
    /// <c>ZonaHoraria</c> es el id IANA ([Decisión 12-8], CAJAS-PR3).</summary>
    public sealed record ActualizarSucursalPayload(
        string? Nombre,
        TipoSucursal? Tipo = null,
        string? ClaveAw = null,
        bool? LimpiarClaveAw = null,
        string? ZonaHoraria = null);
}
