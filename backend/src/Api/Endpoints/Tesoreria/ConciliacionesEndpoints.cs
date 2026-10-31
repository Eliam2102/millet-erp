using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Conciliaciones;
using Millet.Tesoreria.Domain.Conciliaciones;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP para el ciclo de vida de la conciliación bancaria (T9.2):
/// Importar extracto BBVA, emparejamiento automático (3 pasos) y manual,
/// alta asistida de comisiones/intereses/depósitos, partidas en tránsito,
/// cierre cuadrado (diferencia 0) con acta PDF, y reapertura con motivo.
/// </summary>
public static class ConciliacionesEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaConciliacionesEndpoints(this IEndpointRouteBuilder app)
    {
        var conciliaciones = app
            .MapGroup("/api/v1/tesoreria/conciliaciones")
            .WithTags("Tesoreria - Conciliacion")
            .RequireAuthorization();

        // 1. Listar conciliaciones
        conciliaciones.MapGet("/", async (
            [FromQuery] Guid? cuentaBancariaId,
            [FromQuery] int? anio,
            [FromQuery] int? mes,
            [FromQuery] EstadoConciliacion? estado,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var res = await mediator.Send(new ListarConciliacionesQuery(cuentaBancariaId, anio, mes, estado), ct);
            return Results.Ok(res);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("ListarConciliaciones")
        .Produces<IReadOnlyList<ConciliacionResumenResponse>>(StatusCodes.Status200OK);

        // 2. Crear / Abrir conciliación para cuenta y período
        conciliaciones.MapPost("/", async (
            [FromBody] CrearConciliacionCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var res = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/tesoreria/conciliaciones/{res.Id}", res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("CrearConciliacion")
        .Produces<ConciliacionResumenResponse>(StatusCodes.Status201Created);

        // 3. Detalle de conciliación con líneas, bitácora y movimientos
        conciliaciones.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var res = await mediator.Send(new ObtenerConciliacionDetalleQuery(id), ct);
            return Results.Ok(res);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("ObtenerConciliacionDetalle")
        .Produces<ConciliacionDetalleResponse>(StatusCodes.Status200OK);

        // 4. Cargar extracto bancario (BBVA por defecto)
        conciliaciones.MapPost("/{id:guid}/extracto", async (
            Guid id,
            [FromBody] CargarExtractoRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new CargarExtractoCommand(
                id,
                request.ContenidoArchivo,
                request.Formato ?? "BBVA",
                request.SaldoExtracto,
                request.AutoEmparejar ?? true);

            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("CargarExtractoConciliacion")
        .Produces<ConciliacionResumenResponse>(StatusCodes.Status200OK);

        // 5. Correr emparejamiento automático (3 pasos)
        conciliaciones.MapPost("/{id:guid}/emparejar-automatico", async (
            Guid id,
            [FromBody] EmparejarAutomaticoRequest? request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new EmparejarAutomaticoCommand(
                id,
                request?.ToleranciaImporte,
                request?.VentanaDias);

            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("EmparejarAutomaticoConciliacion")
        .Produces<Millet.Tesoreria.Domain.Conciliaciones.Emparejamiento.ResultadoEmparejamiento>(StatusCodes.Status200OK);

        // 6. Emparejamiento manual (1 a 1 y varios a 1)
        conciliaciones.MapPost("/{id:guid}/emparejar-manual", async (
            Guid id,
            [FromBody] EmparejarManualRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new EmparejarManualCommand(id, request.ExtractoLineaId, request.MovimientoIds);
            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("EmparejarManualConciliacion")
        .Produces<ExtractoLineaResponse>(StatusCodes.Status200OK);

        // 7. Desemparejar línea con motivo obligatorio y bitácora (CA9.5 / T9.2-a)
        conciliaciones.MapPost("/{id:guid}/desemparejar", async (
            Guid id,
            [FromBody] DesemparejarLineaRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new DesemparejarLineaCommand(id, request.ExtractoLineaId, request.Motivo);
            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("DesemparejarLineaConciliacion")
        .Produces<ExtractoLineaResponse>(StatusCodes.Status200OK);

        // 8. Alta asistida desde extracto (comisión, interés, depósito no registrado)
        conciliaciones.MapPost("/{id:guid}/alta-asistida", async (
            Guid id,
            [FromBody] AltaAsistidaRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new AltaAsistidaDesdeExtractoCommand(
                id,
                request.ExtractoLineaId,
                request.ConceptoId,
                request.IncluyeIva ?? false,
                request.TasaIva ?? 0.16m,
                request.ConceptoIvaId,
                request.MontoBase,
                request.MontoIva);

            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("AltaAsistidaConciliacion")
        .Produces<ExtractoLineaResponse>(StatusCodes.Status200OK);

        // 9. Marcar partida en tránsito (P9.4)
        conciliaciones.MapPost("/{id:guid}/partidas-transito", async (
            Guid id,
            [FromBody] MarcarPartidaTransitoRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new MarcarPartidaTransitoCommand(
                id,
                request.ExtractoLineaId,
                request.Tipo,
                request.Responsable,
                request.AutorizadaPor);

            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("MarcarPartidaTransito")
        .Produces<ExtractoLineaResponse>(StatusCodes.Status200OK);

        // 10. Cerrar conciliación (Solo cierra con diferencia 0 y genera acta, RN-7)
        conciliaciones.MapPost("/{id:guid}/cerrar", async (
            Guid id,
            [FromBody] CerrarConciliacionRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new CerrarConciliacionCommand(id, request.FirmanteNombre, request.FirmantePuesto);
            byte[] pdfBytes = await mediator.Send(command, ct);
            return Results.File(pdfBytes, "application/pdf", $"acta-conciliacion-{id}.pdf");
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionCerrar)
        .WithName("CerrarConciliacion")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        // 11. Reabrir conciliación cerrada (exige motivo obligatorio)
        conciliaciones.MapPost("/{id:guid}/reabrir", async (
            Guid id,
            [FromBody] ReabrirConciliacionRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new ReabrirConciliacionCommand(id, request.Motivo);
            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionCerrar)
        .WithName("ReabrirConciliacion")
        .Produces<ConciliacionResumenResponse>(StatusCodes.Status200OK);

        // 12. Descargar acta de conciliación en PDF
        conciliaciones.MapGet("/{id:guid}/acta", async (
            Guid id,
            IMediator mediator,
            CancellationToken ct) =>
        {
            byte[] pdfBytes = await mediator.Send(new DescargarActaConciliacionQuery(id), ct);
            return Results.File(pdfBytes, "application/pdf", $"acta-conciliacion-{id}.pdf");
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("DescargarActaConciliacion")
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf");

        return app;
    }
}

public sealed record CargarExtractoRequest(
    string ContenidoArchivo,
    string? Formato = "BBVA",
    decimal? SaldoExtracto = null,
    bool? AutoEmparejar = true);

public sealed record EmparejarAutomaticoRequest(
    decimal? ToleranciaImporte = null,
    int? VentanaDias = null);

public sealed record EmparejarManualRequest(
    Guid ExtractoLineaId,
    IReadOnlyList<Guid> MovimientoIds);

public sealed record DesemparejarLineaRequest(
    Guid ExtractoLineaId,
    string Motivo);

public sealed record AltaAsistidaRequest(
    Guid ExtractoLineaId,
    Guid ConceptoId,
    bool? IncluyeIva = false,
    decimal? TasaIva = 0.16m,
    Guid? ConceptoIvaId = null,
    decimal? MontoBase = null,
    decimal? MontoIva = null);

public sealed record MarcarPartidaTransitoRequest(
    Guid ExtractoLineaId,
    PartidaTransitoTipo Tipo,
    string Responsable,
    Guid? AutorizadaPor = null);

public sealed record CerrarConciliacionRequest(
    string FirmanteNombre,
    string FirmantePuesto);

public sealed record ReabrirConciliacionRequest(
    string Motivo);
