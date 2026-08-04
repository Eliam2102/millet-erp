using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Domain.Conteos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Conteos;

/// <summary>
/// Endpoints de inventario físico (F7-PR1). CRÍTICO — separación de
/// permisos entre captura (contador, sin teórico A6) y comparación
/// (aprobador, con teórico).
/// </summary>
public static class ConteosEndpoints
{
    public static IEndpointRouteBuilder MapConteosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/conteos")
            .WithTags("Almacen")
            .RequireAuthorization();

        // GET / (bandeja).
        group.MapGet("/", async (
            [FromQuery] EstadoConteo? estado,
            [FromQuery] TipoConteo? tipo,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var off = offset is < 0 ? 0 : offset ?? 0;
            var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, 500);
            return Results.Ok(await mediator.Send(
                new ListarConteosQuery(estado, tipo, subAlmacenId, off, lim), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosLeer)
        .WithName("ListarConteos")
        .WithSummary("Listar conteos de inventario (bandeja)")
        .Produces<AlmacenPagedResponse<ConteoListItem>>(StatusCodes.Status200OK);

        // GET /{id} (detalle).
        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var detalle = await mediator.Send(new ObtenerConteoPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosLeer)
        .WithName("ObtenerConteoPorId")
        .Produces<ConteoDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // POST / (Crear conteo).
        group.MapPost("/", async (
            CrearConteoCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var response = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/almacen/conteos/{response.ConteoId}", response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCrear)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CrearConteo")
        .Produces<CrearConteoResponse>(StatusCodes.Status201Created);

        // POST /{id}/iniciar (toma snapshot).
        group.MapPost("/{id:guid}/iniciar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new IniciarConteoCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCrear)
        .WithName("IniciarConteo")
        .Produces(StatusCodes.Status204NoContent);

        // ─── Captura sin sesgo (A6) ────────────────────────────────────────
        //
        // GET /{id}/lineas-para-capturar — endpoint del CONTADOR.
        // NO devuelve cantidad_teorica. Permiso `almacen.inventarios.capturar`
        // — el rol del contador NO tiene `almacen.inventarios.aprobar-*`.

        group.MapGet("/{id:guid}/lineas-para-capturar", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            return Results.Ok(await mediator.Send(new ListarLineasParaCapturarQuery(id), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCapturar)
        .WithName("ListarLineasParaCapturar")
        .WithSummary("Líneas para captura (contador) — SIN cantidad_teorica (A6)")
        .Produces<IReadOnlyList<LineaConteoParaCapturarDto>>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/lineas/{lineaId:guid}/capturar", async (
            Guid id, Guid lineaId,
            CapturarLineaRequest req,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await mediator.Send(new CapturarLineaConteoCommand(id, lineaId, req.CantidadReal), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCapturar)
        .WithName("CapturarLineaConteo")
        .WithSummary("Captura cantidad real (contador, sin sesgo A6)")
        .Produces(StatusCodes.Status204NoContent);

        // GET /{id}/comparacion — endpoint del APROBADOR.
        // Permiso distinto: aprobar-nivel1+ requerido. El contador NO puede
        // acceder a este endpoint.
        group.MapGet("/{id:guid}/comparacion", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            return Results.Ok(await mediator.Send(new ListarLineasComparacionQuery(id), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosAprobarNivel1)
        .WithName("ListarLineasComparacion")
        .WithSummary("Comparación teórico vs real (aprobador). Endpoint distinto al de captura — captura sin sesgo (A6).")
        .Produces<IReadOnlyList<LineaConteoComparacionDto>>(StatusCodes.Status200OK);

        // POST /{id}/enviar-a-conciliacion.
        group.MapPost("/{id:guid}/enviar-a-conciliacion", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new EnviarConteoAConciliacionCommand(id), ct);
            return Results.NoContent();
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenInventariosCapturar)
        .WithName("EnviarConteoAConciliacion")
        .Produces(StatusCodes.Status204NoContent);

        return app;
    }
}

public sealed record CapturarLineaRequest(decimal CantidadReal);
