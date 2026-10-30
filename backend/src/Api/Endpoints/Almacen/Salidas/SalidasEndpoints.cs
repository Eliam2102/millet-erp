using MediatR;
using Millet.SharedKernel.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Salidas.Pdf;
using Millet.Almacen.Domain.Movimientos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Almacen.Salidas;

/// <summary>
/// Endpoints HTTP de salidas del módulo Almacén (F4-PR1). Bajo
/// <c>/api/v1/almacen/salidas</c>. Auth con permisos canónicos
/// <c>almacen.salidas.*</c>. POST requiere Idempotency-Key.
/// </summary>
public static class SalidasEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapSalidasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/salidas")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar Salidas (bandeja) ---
        group.MapGet("/", async (
            [FromQuery] EstadoMovimiento? estado,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? rqId,
            [FromQuery] Guid? personaDestinatariaId,
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] bool? soloVales,
            [FromQuery] bool? noRegularizados,
            [FromQuery] bool? soloVencidos,
            [FromQuery] bool? soloPorVencer,
            [FromQuery] string? q,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarSalidasQuery(estado, subAlmacenId, rqId, personaDestinatariaId,
                    desde, hasta, soloVales, noRegularizados, off, lim, soloVencidos, soloPorVencer, q), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ListarSalidas")
        .WithSummary("Listar salidas (bandeja con filtros)")
        .Produces<AlmacenPagedResponse<SalidaListItem>>(StatusCodes.Status200OK);

        // --- Conteos por estado (tabs de la bandeja; mismos filtros salvo estado) ---
        group.MapGet("/conteos-por-estado", async (
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? rqId,
            [FromQuery] Guid? personaDestinatariaId,
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] bool? soloVales,
            [FromQuery] bool? noRegularizados,
            [FromQuery] bool? soloVencidos,
            [FromQuery] bool? soloPorVencer,
            [FromQuery] string? q,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(
                new ConteosSalidasPorEstadoQuery(subAlmacenId, rqId, personaDestinatariaId, desde, hasta,
                    soloVales, noRegularizados, soloVencidos, soloPorVencer, q), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ConteosSalidasPorEstado")
        .WithSummary("Conteo de salidas por estado, con los filtros de la bandeja salvo el estado")
        .Produces<IReadOnlyList<ConteoPorEstadoItem>>(StatusCodes.Status200OK);

        // --- Obtener Salida por Id ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("salida_almacen", id, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales, ct);
            var detalle = await mediator.Send(new ObtenerSalidaPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ObtenerSalidaPorId")
        .WithSummary("Obtener salida con líneas")
        .Produces<SalidaDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Comprobante de salida (formato M2 de Millet, 86e3tferp) ---
        group.MapGet("/{id:guid}/comprobante", async (
            Guid id,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            HttpResponse response,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("salida_almacen", id, PermisosCanonicos.AlmacenSalidasLeerTodasSucursales, ct);
            var pdf = await mediator.Send(new ComprobanteSalidaPdfQuery(id), ct);
            if (pdf is null) return Results.NotFound();
            // Se dibuja al momento con los datos vigentes; no se guarda.
            response.Headers.CacheControl = "no-store";
            return Results.File(pdf.Contenido, "application/pdf", pdf.NombreArchivo);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasLeerTodas)
        .WithName("ObtenerComprobanteSalida")
        .WithSummary("PDF del comprobante de salida con el formato de Millet")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Registrar Salida Variante A (con RQ) ---
        group.MapPost("/", async (
            RegistrarSalidaConRequisicionCommand command,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("requisicion", command.RequisicionId, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            foreach (var bin in (command.Lineas ?? []).Select(l => l.UbicacionId).OfType<Guid>().Distinct())
                await documentos.VerificarAsync("ubicacion_almacen", bin, PermisosCanonicos.AlmacenSalidasGestionarTodasSucursales, ct);
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/salidas/{response.SalidaId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenSalidasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarSalidaConRequisicion")
        .WithSummary("Registrar salida normal con RQ (consume reserva si existe)")
        .Produces<RegistrarSalidaResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);
        return (off, lim);
    }
}
