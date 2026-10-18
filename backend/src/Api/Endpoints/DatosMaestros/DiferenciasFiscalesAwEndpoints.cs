using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.DatosMaestros.Domain;
using Millet.Identidad.Domain;
using Millet.Integraciones.Aw.Application.DiferenciasFiscales;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.DatosMaestros;

public sealed record MarcarDiferenciaFiscalAwRevisadaRequest(string? Tipo, Guid EntidadId, string? Campo, string? Huella, string? Nota);

/// <summary>
/// U1.8b (F1-FAC-14): reporte de diferencias fiscales A+W vs ERP. Solo lee A+W por los puertos existentes y
/// compara; "Corregir" se hace en el detalle del cliente/producto con sus permisos propios. Permisos:
/// <c>datos_maestros.diferencias-fiscales-aw.leer</c> (reporte y corridas) y <c>.revisar</c> (marcar revisado y
/// guardar una corrida). Master cross-sucursal: sin alcance por sucursal (ADR-0051 no aplica).
/// </summary>
public static class DiferenciasFiscalesAwEndpoints
{
    private const string Base = "/api/v1/datos-maestros/diferencias-fiscales-aw";

    public static IEndpointRouteBuilder MapDiferenciasFiscalesAwEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Base).WithTags("DatosMaestros");
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer;
        var revisar = PermissionPolicyProvider.Prefix + PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwRevisar;

        group.MapGet("/", async (
            [FromQuery] string? tipo,
            [FromQuery] string? campo,
            [FromQuery] string? estado,
            [FromQuery] bool? incluirRevisadas,
            ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(new ObtenerDiferenciasFiscalesAwQuery(
                Vacio(tipo), Vacio(campo), Vacio(estado), incluirRevisadas ?? false), ct)))
        .RequireAuthorization(leer)
        .WithName("ObtenerDiferenciasFiscalesAw")
        .WithSummary("Calcula en vivo las diferencias fiscales entre A+W (o la regla fiscal, en productos) y el ERP")
        .Produces<ReporteResponse<FilaDiferenciaFiscalAw>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/revisiones", async (
            [FromBody] MarcarDiferenciaFiscalAwRevisadaRequest body,
            ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(new MarcarDiferenciaFiscalAwRevisadaCommand(
                body.Tipo ?? string.Empty, body.EntidadId, body.Campo ?? string.Empty, body.Huella ?? string.Empty, body.Nota), ct)))
        .RequireAuthorization(revisar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("MarcarDiferenciaFiscalAwRevisada")
        .WithSummary("Marca revisada una diferencia; reaparece cuando cambia cualquiera de los dos valores")
        .Produces<RevisionDiferenciaFiscalAwDto>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/corridas/ultima", async (
            [FromQuery] bool? incluirRevisadas,
            ISender sender,
            CancellationToken ct) =>
            Results.Ok(await sender.Send(new ObtenerUltimaCorridaDiferenciasFiscalesAwQuery(incluirRevisadas ?? false), ct)))
        .RequireAuthorization(leer)
        .WithName("ObtenerUltimaCorridaDiferenciasFiscalesAw")
        .WithSummary("Última corrida guardada (programada o manual) y resumen de la última programada")
        .Produces<UltimaCorridaDiferenciasFiscalesAw>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/corridas", async (ISender sender, ICurrentUserContext user, CancellationToken ct) =>
        {
            var actor = user.UserName ?? user.Email ?? user.UserId?.ToString() ?? "desconocido";
            return Results.Ok(await sender.Send(new EjecutarCorridaDiferenciasFiscalesAwCommand(OrigenCorridaDiferenciaFiscal.Manual, actor), ct));
        })
        .RequireAuthorization(revisar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("EjecutarCorridaDiferenciasFiscalesAw")
        .WithSummary("Calcula el reporte completo y lo guarda como corrida manual")
        .Produces<CorridaDiferenciasFiscalesAwResumen>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static string? Vacio(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
