using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Endpoints.Adjuntos;
using Millet.Api.Web;
using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// Pólizas contables (F1-CON-04: C1.3 captura y autorización, K10.9 numeración por tipo, K10.3 anulación en negativo) bajo
/// <c>/api/v1/contabilidad/polizas</c>. ETag en GET por id; <c>If-Match</c> (versión) e <c>Idempotency-Key</c> en las mutaciones.
/// La póliza es de la empresa; el alcance por sucursal se verifica en cada partida al capturar y operar (ADR-0051).
/// </summary>
public static class ContabilidadPolizasEndpoints
{
    public sealed record AnularPolizaRequest(string Motivo, int? PeriodoAnio, int? PeriodoNumero);

    public static IEndpointRouteBuilder MapContabilidadPolizasEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var capturar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasCapturar;
        var autorizar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasAutorizar;
        var anular = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasAnular;

        var g = app.MapGroup("/api/v1/contabilidad/polizas").WithTags("Contabilidad · Pólizas").RequireAuthorization();

        g.MapGet("/", async (
            [FromQuery] int? anio, [FromQuery] int? numero, [FromQuery] Guid? tipoOperacionId, [FromQuery] EstadoPoliza? estado,
            [FromQuery] OrigenPoliza? origen, [FromQuery] string? q, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarPolizasQuery(anio, numero, tipoOperacionId, estado, origen, q, offset ?? 0, limit ?? 50), ct)))
        .RequireAuthorization(leer).WithName("ListarPolizasContables")
        .WithSummary("Bandeja de pólizas (más reciente primero); q busca por folio TT-000123 o por concepto")
        .Produces<PagedResponse<PolizaResumenResponse>>().ProducesValidationProblem();

        g.MapGet("/saldos", async ([FromQuery] int anio, [FromQuery] int numero, [FromQuery] Guid? cuentaId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new SaldosPeriodoQuery(anio, numero, cuentaId), ct)))
        .RequireAuthorization(leer).WithName("SaldosPeriodoPolizas")
        .WithSummary("Cargos, abonos y saldo del periodo por cuenta (autorizadas, anuladas y sus anulaciones; sin borradores)")
        .Produces<IReadOnlyList<SaldoCuentaPeriodoResponse>>().ProducesValidationProblem();

        g.MapGet("/{id:guid}", async (Guid id, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new ObtenerPolizaQuery(id), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .RequireAuthorization(leer).WithName("ObtenerPolizaContable")
        .Produces<PolizaResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/", async (PolizaManualRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new CrearPolizaCommand(b), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/polizas/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("CrearPolizaContable")
        .WithSummary("Captura una póliza manual en borrador (valida cuentas, dimensiones y periodo; el cuadre se exige al autorizar)")
        .Produces<PolizaResponse>(StatusCodes.Status201Created).ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPut("/{id:guid}", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, PolizaManualRequest b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new EditarPolizaCommand(id, version, b), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("EditarPolizaContable")
        .WithSummary("Edita el borrador; la autorizada no se edita (solo se anula)")
        .Produces<PolizaResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapDelete("/{id:guid}", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            await mediator.Send(new DescartarPolizaCommand(id, version), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("DescartarPolizaContable")
        .WithSummary("Descarta un borrador (no tiene número: no deja hueco en el consecutivo)")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapPost("/{id:guid}/autorizar", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new AutorizarPolizaCommand(id, version), ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Ok(r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(autorizar).WithName("AutorizarPolizaContable")
        .WithSummary("Autoriza el borrador: debe cuadrar y no la autoriza quien la capturó; asigna el número del tipo (TT-000123)")
        .Produces<PolizaResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapPost("/{id:guid}/anular", async (
            Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, AnularPolizaRequest b, IMediator mediator, CancellationToken ct) =>
        {
            if (!Helpers.TryParseVersion(ifMatch, out var version)) return Helpers.IfMatchRequerido();
            var r = await mediator.Send(new AnularPolizaCommand(id, version, b.Motivo, b.PeriodoAnio, b.PeriodoNumero), ct);
            return Results.Created($"/api/v1/contabilidad/polizas/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(anular).WithName("AnularPolizaContable")
        .WithSummary("Anula una póliza autorizada con motivo: crea la póliza con las mismas partidas en negativo («Anulación de TT-000123»)")
        .WithDescription("Devuelve la póliza de anulación. Si el periodo de la original ya no admite pólizas, indique periodoAnio y periodoNumero abiertos.")
        .Produces<PolizaResponse>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        g.MapGroup("/{id:guid}").MapAdjuntos(PolizaAdjuntoPropietario.Tipo,
            PermisosCanonicos.ContabilidadPolizasLeer, PermisosCanonicos.ContabilidadPolizasCapturar, PermisosCanonicos.ContabilidadPolizasCapturar);
        return app;
    }
}
