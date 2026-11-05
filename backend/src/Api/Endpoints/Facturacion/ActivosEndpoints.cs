using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Activos.AutorizarVentaActivo;
using Millet.Facturacion.Application.Activos.Queries;
using Millet.Facturacion.Domain.Activos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Endpoints de venta de activos fijos del módulo Facturación (F9). La
/// autorización del Director de Administración y Finanzas es previa a la emisión.
/// <c>/api/v1/facturacion/activos</c>.
/// </summary>
public static class ActivosEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionActivosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/activos")
            .WithTags("Facturacion");

        // POST: el Director de Administración y Finanzas autoriza la venta de un activo fijo (previo a emitir).
        group.MapPost("/autorizar", async (
            AutorizarVentaActivoCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/facturacion/activos/{response.AutorizacionId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionActivosAutorizar)
        .WithName("AutorizarVentaActivo")
        .WithSummary("Autoriza la venta de un activo fijo (Director de Administración y Finanzas)")
        .Produces<AutorizarVentaActivoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // GET: bandeja de autorizaciones (por defecto las Autorizadas pendientes de usar). B13.
        group.MapGet("/autorizaciones", async (
            [FromQuery] EstadoAutorizacionActivo? estado, IMediator mediator, CancellationToken ct) =>
        {
            var items = await mediator.Send(new BandejaAutorizacionesActivoQuery(estado), ct);
            return Results.Ok(items);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionActivosAutorizar)
        .WithName("BandejaAutorizacionesActivo")
        .WithSummary("Bandeja de autorizaciones de venta de activos (Director de Administración y Finanzas)")
        .Produces<IReadOnlyList<AutorizacionActivoItem>>(StatusCodes.Status200OK);

        group.MapPost("/autorizaciones/{id:guid}/rechazar", async (Guid id, RechazoBody datos, HttpRequest req, FacturacionDbContext db, ICurrentUserContext user, IClock clock, CancellationToken ct) =>
        {
            if (!int.TryParse(req.Headers.IfMatch.ToString().Trim('"'), out var version) || version < 1) return Results.Problem(statusCode:428,title:"Envíe la versión vigente de la autorización.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var a = await db.AutorizacionesVentaActivo.SingleAsync(x => x.Id == id, ct);
            var llave = $"AF-LIBRO-{a.EmpresaId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({llave}, 0))", ct);
            await db.Entry(a).ReloadAsync(ct);
            if (a.Version != version) throw new ConcurrencyException("AutorizacionVentaActivo", id);
            a.Rechazar(user.UserId ?? Guid.Empty, datos.Motivo, clock.UtcNow);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Results.Ok(a);
        }).RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionActivosAutorizar)
            .WithMetadata(new RequireIdempotencyKeyAttribute());
        return app;
    }
    public sealed record RechazoBody(string Motivo);
}
