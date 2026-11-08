using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.ConciliacionSat;
using Millet.Facturacion.Domain.ConciliacionSat;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// FAC-10 · conciliación de CFDI emitidos contra el SAT: se sube el archivo de
/// metadatos (portal o descarga masiva) con el periodo, se guarda la corrida con
/// sus diferencias y cada diferencia se marca como revisada. Es de toda la
/// empresa, no de una sucursal: la piden los permisos corporativos de reportes
/// de Facturación (supuesto a validar mientras no exista un permiso propio).
/// </summary>
public static class ConciliacionSatEndpoints
{
    private const long TamanoMaximo = 20 * 1024 * 1024;

    public static IEndpointRouteBuilder MapFacturacionConciliacionSatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/conciliacion-sat")
            .WithTags("Facturacion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReportesLeerTodasSucursales);

        group.MapPost("/", async (
            IFormFile? archivo, [FromForm] DateOnly? desde, [FromForm] DateOnly? hasta, IMediator mediator, CancellationToken ct) =>
        {
            if (archivo is null || archivo.Length == 0)
                throw new BusinessRuleException("ARCHIVO_REQUERIDO", "Envía el archivo de metadatos del SAT en el campo 'archivo'.");
            if (archivo.Length > TamanoMaximo)
                throw new BusinessRuleException("CONCILIACION_SAT_ARCHIVO_GRANDE", "El archivo del SAT no debe pasar de 20 MB.");
            if (desde is not DateOnly d || hasta is not DateOnly h)
                throw new BusinessRuleException("CONCILIACION_SAT_PERIODO_REQUERIDO", "Indica el periodo (desde y hasta) que cubre el archivo.");
            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            return Results.Ok(await mediator.Send(new ConciliarCfdiSatCommand(archivo.FileName, memoria.ToArray(), d, h), ct));
        })
        .DisableAntiforgery()
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ConciliarCfdiSat")
        .WithSummary("Concilia los CFDI emitidos del archivo de metadatos del SAT contra los timbrados del ERP")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ConciliacionSatResponse>()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/", async ([FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConciliacionesSatQuery(limit ?? 20), ct)))
        .WithName("ListarConciliacionesSat")
        .WithSummary("Corridas de la conciliación contra el SAT, de la más reciente a la más vieja")
        .Produces<IReadOnlyList<ConciliacionSatResponse>>();

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConciliacionSatQuery(id), ct)))
        .WithName("ObtenerConciliacionSat")
        .Produces<ConciliacionSatResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/diferencias", async (
            Guid id, [FromQuery] TipoDiferenciaSat? tipo, [FromQuery] bool? revisada, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DiferenciasConciliacionSatQuery(id, tipo, revisada), ct)))
        .WithName("ListarDiferenciasConciliacionSat")
        .WithSummary("Diferencias de una corrida, filtrables por tipo y por revisada")
        .Produces<IReadOnlyList<DiferenciaSatResponse>>();

        group.MapPost("/{id:guid}/diferencias/{diferenciaId:guid}/revisada", async (
            Guid id, Guid diferenciaId, MarcarRevisadaRequest? body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new MarcarDiferenciaSatRevisadaCommand(id, diferenciaId, body?.Nota), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("MarcarDiferenciaSatRevisada")
        .WithSummary("Marca una diferencia de la conciliación como revisada, con nota opcional")
        .Produces<DiferenciaSatResponse>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record MarcarRevisadaRequest(string? Nota);
}
