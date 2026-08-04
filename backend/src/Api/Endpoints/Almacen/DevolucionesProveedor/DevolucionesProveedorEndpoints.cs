using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.DevolucionesProveedor;

/// <summary>
/// Endpoints HTTP del sub-flujo 8.B (Devolución a proveedor, F6-PR1).
/// Bajo <c>/api/v1/almacen/devoluciones-proveedor</c>. Flujo completo:
/// Iniciar → AdjuntarEvidencia → SolicitarAutorizacion → Autorizar/Rechazar
/// → RegistrarSalida (publica OcDevolucionRegistradaEvent).
/// </summary>
public static class DevolucionesProveedorEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapDevolucionesProveedorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/devoluciones-proveedor")
            .WithTags("Almacen")
            .RequireAuthorization();

        // GET / (bandeja)
        group.MapGet("/", async (
            [FromQuery] EstadoDevolucionProveedor? estado,
            [FromQuery] Guid? proveedorId,
            [FromQuery] bool? soloPendientesNcFiscal,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarDevolucionesProveedorQuery(estado, proveedorId, soloPendientesNcFiscal, off, lim), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasLeer)
        .WithName("ListarDevolucionesProveedor")
        .WithSummary("Listar devoluciones a proveedor (bandeja con filtros, incluye \"pendientes de NC fiscal\")")
        .Produces<AlmacenPagedResponse<DevolucionProveedorListItem>>(StatusCodes.Status200OK);

        // GET /{id}
        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerDevolucionProveedorPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesInternasLeer)
        .WithName("ObtenerDevolucionProveedorPorId")
        .WithSummary("Detalle de devolución a proveedor con líneas + evidencias")
        .Produces<DevolucionProveedorDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST / (Iniciar)
        group.MapPost("/", async (
            IniciarDevolucionAProveedorCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/devoluciones-proveedor/{response.DevolucionId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorIniciar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("IniciarDevolucionAProveedor")
        .WithSummary("Iniciar devolución a proveedor (sub-flujo 8.B)")
        .Produces<IniciarDevolucionAProveedorResponse>(StatusCodes.Status201Created);

        // POST /{id}/evidencias
        group.MapPost("/{id:guid}/evidencias", async (
            Guid id,
            AdjuntarEvidenciaRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new AdjuntarEvidenciaDevolucionAProveedorCommand(
                id, req.TipoEvidencia, req.NombreArchivo, req.BlobRef, req.Comentario), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorIniciar)
        .WithName("AdjuntarEvidenciaDevolucionProveedor")
        .WithSummary("Adjuntar evidencia (foto, email, etc.) a la devolución")
        .Produces(StatusCodes.Status204NoContent);

        // POST /{id}/solicitar-autorizacion
        group.MapPost("/{id:guid}/solicitar-autorizacion", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new SolicitarAutorizacionDevolucionAProveedorCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorIniciar)
        .WithName("SolicitarAutorizacionDevolucionProveedor")
        .WithSummary("Solicitar autorización a Dirección")
        .Produces(StatusCodes.Status204NoContent);

        // POST /{id}/autorizar (Dirección)
        group.MapPost("/{id:guid}/autorizar", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new AutorizarDevolucionAProveedorCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorAutorizar)
        .WithName("AutorizarDevolucionProveedor")
        .WithSummary("Autorizar devolución (Dirección) — requiere evidencia adjunta")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // POST /{id}/rechazar
        group.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            RechazarDevolucionRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new RechazarDevolucionAProveedorCommand(id, req.MotivoRechazo), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorAutorizar)
        .WithName("RechazarDevolucionProveedor")
        .WithSummary("Rechazar devolución (Dirección)")
        .Produces(StatusCodes.Status204NoContent);

        // POST /{id}/registrar-salida (Almacenista)
        group.MapPost("/{id:guid}/registrar-salida", async (
            Guid id,
            RegistrarSalidaRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(new RegistrarSalidaDevolucionAProveedorCommand(
                id, req.SubAlmacenId, req.FechaMovimiento, req.Bins), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenDevolucionesProveedorRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarSalidaDevolucionProveedor")
        .WithSummary("Registrar salida física + publicar OcDevolucionRegistradaEvent (ciclo bidireccional con CxP)")
        .Produces<RegistrarSalidaDevolucionAProveedorResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);
        return (off, lim);
    }
}

public sealed record AdjuntarEvidenciaRequest(
    string TipoEvidencia,
    string NombreArchivo,
    string BlobRef,
    string? Comentario);

public sealed record RechazarDevolucionRequest(string MotivoRechazo);

/// <summary>
/// GAP-4 (verificación e2e 2026-07-15): <c>Bins</c> es la ubicación de
/// origen por línea de devolución (C7.2b). Sin ella el trigger de saldos
/// drena la ÚNICA y truena con <c>SALDO_INEXISTENTE</c> cuando el stock
/// vive en un rack. El command ya la soportaba; el DTO la descartaba.
/// </summary>
public sealed record RegistrarSalidaRequest(
    Guid SubAlmacenId,
    DateOnly FechaMovimiento,
    IReadOnlyList<DevolucionProveedorSalidaLineaBin>? Bins = null);
