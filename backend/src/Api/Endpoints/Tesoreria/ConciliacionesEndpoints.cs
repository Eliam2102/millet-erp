using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
            IdentidadDbContext identidadDb,
            CancellationToken ct) =>
        {
            var res = await mediator.Send(new ObtenerConciliacionDetalleQuery(id), ct);
            var ids = res.Bitacora.Select(b => b.UsuarioId).Append(res.VistoBuenoPor ?? Guid.Empty).Distinct().ToArray();
            var nombres = await identidadDb.Usuarios.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Nombre, ct);
            return Results.Ok(res with
            {
                Bitacora = res.Bitacora.Select(b => b with { UsuarioNombre = nombres.GetValueOrDefault(b.UsuarioId) }).ToList(),
                VistoBuenoPorNombre = res.VistoBuenoPor is Guid vb ? nombres.GetValueOrDefault(vb) : null,
            });
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("ObtenerConciliacionDetalle")
        .Produces<ConciliacionDetalleResponse>(StatusCodes.Status200OK);

        // 4. Cargar extracto bancario (sin formato: perfil de extracto de la cuenta)
        conciliaciones.MapPost("/{id:guid}/extracto", async (
            Guid id,
            [FromBody] CargarExtractoRequest request,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var command = new CargarExtractoCommand(
                id,
                request.ContenidoArchivo,
                request.Formato,
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
                request.MontoIva,
                request.AreaId,
                request.EquipoId);

            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("AltaAsistidaConciliacion")
        .Produces<ExtractoLineaResponse>(StatusCodes.Status200OK);

        // 8b. H1 paso 3: qué exige la cuenta del concepto (regla C-03) y áreas o equipos de la sucursal de la cuenta bancaria
        conciliaciones.MapGet("/{id:guid}/alta-asistida/dimensiones", async (
            Guid id, [FromQuery] Guid conceptoId, [FromQuery] DateOnly? fecha, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DimensionesAltaQuery(id, conceptoId, fecha), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("DimensionesAltaAsistida")
        .WithSummary("Área y equipo que exige la cuenta del concepto bancario (regla C-03)")
        .Produces<DimensionesAltaResponse>(StatusCodes.Status200OK);

        conciliaciones.MapGet("/{id:guid}/alta-asistida/centros", async (
            Guid id, [FromQuery] string? nivel, [FromQuery] Guid? dim2Id, [FromQuery] string? q, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CentrosAltaQuery(id, string.Equals(nivel, "Dim3", StringComparison.OrdinalIgnoreCase), dim2Id, q,
                Math.Clamp(limit ?? 50, 1, 200)), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionOperar)
        .WithName("CentrosAltaAsistida")
        .WithSummary("Áreas (Dim2) o equipos (Dim3) activos de la sucursal de la cuenta bancaria")
        .Produces<IReadOnlyList<Millet.Contabilidad.Application.Dimensiones.CentroOpcionDto>>(StatusCodes.Status200OK);

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
                request.Causa,
                request.FechaPrevistaResolucion);

            var res = await mediator.Send(command, ct);
            return Results.Ok(res);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionAutorizarTransito)
        .WithName("MarcarPartidaTransito")
        .Produces<ExtractoLineaResponse>(StatusCodes.Status200OK);

        // 9b. Movimiento de libros en tránsito: cheque no cobrado o depósito del último día (M-201, Javier Canché 11-nov)
        conciliaciones.MapPost("/{id:guid}/movimientos-transito", async (Guid id, [FromBody] MovimientoTransitoRequest request,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new MarcarMovimientoTransitoCommand(id, request.MovimientoBancarioId, request.Tipo,
                request.Causa, request.FechaPrevistaResolucion), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionAutorizarTransito)
        .WithName("MarcarMovimientoTransito")
        .Produces<PartidaTransitoLibroResponse>(StatusCodes.Status200OK);

        conciliaciones.MapDelete("/{id:guid}/movimientos-transito/{movimientoId:guid}", async (Guid id, Guid movimientoId,
            IMediator mediator, CancellationToken ct) =>
        {
            await mediator.Send(new QuitarMovimientoTransitoCommand(id, movimientoId), ct);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionAutorizarTransito)
        .WithName("QuitarMovimientoTransito")
        .Produces(StatusCodes.Status204NoContent);

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
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionReabrir)
        .WithName("ReabrirConciliacion")
        .Produces<ConciliacionResumenResponse>(StatusCodes.Status200OK);

        // 11b. Visto bueno de Contabilidad (M-202)
        conciliaciones.MapPost("/{id:guid}/visto-bueno", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DarVistoBuenoConciliacionCommand(id), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionRevisar)
        .WithName("VistoBuenoConciliacion")
        .Produces<ConciliacionResumenResponse>(StatusCodes.Status200OK);

        // 11c. Contabilidad devuelve a Tesorería con motivo (M-202)
        conciliaciones.MapPost("/{id:guid}/devolver", async (Guid id, [FromBody] ReabrirConciliacionRequest request, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DevolverConciliacionCommand(id, request.Motivo), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaConciliacionRevisar)
        .WithName("DevolverConciliacion")
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
    string? Formato = null,
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
    decimal? MontoIva = null,
    Guid? AreaId = null,
    Guid? EquipoId = null);

public sealed record MarcarPartidaTransitoRequest(
    Guid ExtractoLineaId,
    PartidaTransitoTipo Tipo,
    string Responsable,
    string Causa,
    DateOnly FechaPrevistaResolucion);

public sealed record CerrarConciliacionRequest(
    string FirmanteNombre,
    string FirmantePuesto);

public sealed record ReabrirConciliacionRequest(
    string Motivo);

public sealed record MovimientoTransitoRequest(
    Guid MovimientoBancarioId,
    PartidaTransitoTipo Tipo,
    string Causa,
    DateOnly FechaPrevistaResolucion);
