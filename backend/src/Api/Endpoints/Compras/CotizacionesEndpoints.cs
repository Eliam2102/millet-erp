using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Api.Web;
using Millet.Compras.Application.Cotizaciones;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Cotizaciones de proveedores de una requisición (F1-COM-02). El alcance por sucursal es el de la
/// requisición (ADR-0051); solo Compras (<c>compras.cotizaciones.gestionar</c>) escribe.
/// </summary>
public static class CotizacionesEndpoints
{
    public sealed record RegistrarCotizacionRequest(
        Guid ProveedorId, string? FolioProveedor, string Moneda, DateOnly FechaCotizacion, DateOnly VigenteHasta,
        string? CondicionesCredito, IReadOnlyList<LineaCotizacionRequest> Lineas);

    public static IEndpointRouteBuilder MapCotizacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var rq = app.MapGroup("/api/v1/compras/requisiciones/{id:guid}/cotizaciones").WithTags("Compras");

        rq.MapGet("/", async (
            Guid id, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser, ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await RqSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            return Results.Ok(await mediator.Send(new ComparativoCotizacionesQuery(id), cancellationToken));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasCotizacionesLeer)
        .WithName("ObtenerComparativoCotizacionesCompra")
        .WithSummary("Comparativo de cotizaciones de una requisición")
        .Produces<ComparativoCotizacionesResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        rq.MapPost("/", async (
            Guid id, [FromBody] RegistrarCotizacionRequest body, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser, ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await RqSucursalScope.VerificarAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken, escritura: true);
            var r = await mediator.Send(new RegistrarCotizacionCommand(id, body.ProveedorId, body.FolioProveedor, body.Moneda,
                body.FechaCotizacion, body.VigenteHasta, body.CondicionesCredito, body.Lineas), cancellationToken);
            return Results.Created($"/api/v1/compras/cotizaciones/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasCotizacionesGestionar)
        .WithName("RegistrarCotizacionCompra")
        .WithSummary("Registrar la cotización de un proveedor para una requisición")
        .Produces<CotizacionGuardadaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        var cot = app.MapGroup("/api/v1/compras/cotizaciones").WithTags("Compras");
        cot.MapGroup("/{id:guid}").MapAdjuntos("cotizacion_compra",
            PermisosCanonicos.ComprasCotizacionesLeer, PermisosCanonicos.ComprasCotizacionesGestionar,
            PermisosCanonicos.ComprasCotizacionesGestionar);
        cot.MapPut("/{id:guid}", async (
            Guid id, [FromBody] RegistrarCotizacionRequest body, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser, ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await VerificarAlcanceAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            var r = await mediator.Send(new EditarCotizacionCommand(id, body.ProveedorId, body.FolioProveedor, body.Moneda,
                body.FechaCotizacion, body.VigenteHasta, body.CondicionesCredito, body.Lineas), cancellationToken);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasCotizacionesGestionar)
        .WithName("EditarCotizacion")
        .WithSummary("Editar una cotización antes de elegir")
        .Produces<CotizacionGuardadaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cot.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator,
            ComprasDbContext scopeDb, ICurrentUserContext scopeUser, ICurrentUserPermissions scopePermisos, IUsuarioSucursalReadPort scopeSucursales,
            CancellationToken cancellationToken) =>
        {
            await VerificarAlcanceAsync(id, scopeDb, scopeUser, scopePermisos, scopeSucursales, cancellationToken);
            await mediator.Send(new EliminarCotizacionCommand(id), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasCotizacionesGestionar)
        .WithName("EliminarCotizacion")
        .WithSummary("Eliminar una cotización antes de elegir")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static async Task VerificarAlcanceAsync(
        Guid cotizacionId, ComprasDbContext db, ICurrentUserContext u, ICurrentUserPermissions p,
        IUsuarioSucursalReadPort s, CancellationToken ct)
    {
        var rqId = await db.Cotizaciones.AsNoTracking().Where(c => c.Id == cotizacionId)
            .Select(c => (Guid?)c.RequisicionId).FirstOrDefaultAsync(ct)
            ?? throw new EntityNotFoundException("COTIZACION_NO_ENCONTRADA", $"No se encontró la cotización '{cotizacionId}'.");
        await RqSucursalScope.VerificarAsync(rqId, db, u, p, s, ct, escritura: true);
    }
}
