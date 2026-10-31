using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Corridas;
using Millet.Tesoreria.Domain.Corridas;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints de la corrida de pagos, Parte A (T9.1, 86e3ke5bt): armar, enviar a autorización, firmar, rechazar y
/// cancelar, más el parámetro de firmas por monto y moneda. Transiciones con <c>X-Expected-Version</c> (ADR-0012) e
/// Idempotency-Key (ADR-0020). El oficio y la ejecución llegan en la Parte B (86e3jvvhf).
/// </summary>
public static class CorridasEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaCorridasEndpoints(this IEndpointRouteBuilder app)
    {
        var corridas = app.MapGroup("/api/v1/tesoreria/corridas").WithTags("Tesoreria").RequireAuthorization();
        const string P = PermissionPolicyProvider.Prefix;

        // Lectura: `tesoreria.pasivos.ver` (el RBAC de ruta exige una sola política; firmantes y armadores la tienen en Tesorería).

        corridas.MapGet("/", async ([FromQuery] EstadoCorridaPago? estado, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarCorridasQuery(estado, offset ?? 0, limit ?? 50), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaPasivosVer)
            .WithName("ListarCorridasPago")
            .Produces<PagedResponse<CorridaResumenResponse>>();

        corridas.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerCorridaQuery(id), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaPasivosVer)
            .WithName("ObtenerCorridaPago")
            .Produces<CorridaResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        corridas.MapGet("/candidatos", async ([FromQuery] string moneda, [FromQuery] Guid? proveedorId, [FromQuery] DateOnly? venceDesde,
            [FromQuery] DateOnly? venceHasta, [FromQuery] int? offset, [FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new PasivosParaCorridaQuery(moneda, proveedorId, venceDesde, venceHasta, offset ?? 0, limit ?? 100), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasCrear)
            .WithName("ListarPasivosParaCorrida")
            .Produces<PagedResponse<PasivoParaCorridaResponse>>();

        corridas.MapPost("/", async ([FromBody] CrearCorridaCommand command, IMediator mediator, DocumentoSucursalScope scope, CancellationToken ct) =>
        {
            await VerificarSucursalesAsync(scope, command.Facturas, ct);
            var r = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/tesoreria/corridas/{r.Id}", r);
        })
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasCrear)
            .WithName("CrearCorridaPago")
            .Produces<CorridaResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        corridas.MapPost("/{id:guid}/pasivos", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            [FromBody] AgregarPasivosBody body, IMediator mediator, DocumentoSucursalScope scope, CancellationToken ct) =>
        {
            if (v is not int version) return SinVersion();
            await VerificarSucursalesAsync(scope, body.Facturas, ct);
            return Results.Ok(await mediator.Send(new AgregarPasivosCorridaCommand(id, body.Facturas, version), ct));
        })
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasCrear)
            .WithName("AgregarPasivosCorridaPago").Produces<CorridaResponse>();

        corridas.MapDelete("/{id:guid}/pasivos/{facturaProveedorId:guid}", async (Guid id, Guid facturaProveedorId,
            [FromHeader(Name = "X-Expected-Version")] int? v, IMediator mediator, CancellationToken ct) =>
            v is not int version ? SinVersion() : Results.Ok(await mediator.Send(new QuitarPasivoCorridaCommand(id, facturaProveedorId, version), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasCrear)
            .WithName("QuitarPasivoCorridaPago").Produces<CorridaResponse>();

        corridas.MapPost("/{id:guid}/enviar-autorizacion", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            IMediator mediator, CancellationToken ct) =>
            v is not int version ? SinVersion() : Results.Ok(await mediator.Send(new EnviarCorridaAAutorizacionCommand(id, version), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasCrear)
            .WithName("EnviarCorridaPagoAAutorizacion").Produces<CorridaResponse>();

        corridas.MapPost("/{id:guid}/autorizar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            IMediator mediator, CancellationToken ct) =>
            v is not int version ? SinVersion() : Results.Ok(await mediator.Send(new AutorizarCorridaCommand(id, version), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasAutorizar)
            .WithName("AutorizarCorridaPago").Produces<CorridaResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        corridas.MapPost("/{id:guid}/rechazar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            [FromBody] MotivoBody body, IMediator mediator, CancellationToken ct) =>
            v is not int version ? SinVersion() : Results.Ok(await mediator.Send(new RechazarCorridaCommand(id, body.Motivo, version), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasAutorizar)
            .WithName("RechazarCorridaPago").Produces<CorridaResponse>();

        corridas.MapPost("/{id:guid}/cancelar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            [FromBody] MotivoBody body, IMediator mediator, CancellationToken ct) =>
            v is not int version ? SinVersion() : Results.Ok(await mediator.Send(new CancelarCorridaCommand(id, body.Motivo, version), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasCrear)
            .WithName("CancelarCorridaPago").Produces<CorridaResponse>();

        // T9.1-B: oficio solo de la corrida autorizada (el servidor valida el estado); el PDF se arma en el cliente (ADR-0036).
        corridas.MapPost("/{id:guid}/oficio", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GenerarOficioCorridaCommand(id), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaPasivosVer)
            .WithName("GenerarOficioCorridaPago").Produces<Millet.Tesoreria.Application.Reportes.Comun.ReporteJsonResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // T9.3 (86e3ke5ck): Layout de pago en lote por banco (BBVA primero, configurable).
        corridas.MapPost("/{id:guid}/layout", async (Guid id, [FromQuery] string? banco, [FromQuery] TipoPagoLayout? tipoPago,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GenerarLayoutCorridaCommand(id, banco ?? "BBVA", tipoPago ?? TipoPagoLayout.NacionalSpei), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasEjecutar)
            .WithName("GenerarLayoutCorridaPago")
            .Produces<LayoutCorridaResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        corridas.MapGet("/{id:guid}/layout/descargar", async (Guid id, [FromQuery] string? banco, [FromQuery] TipoPagoLayout? tipoPago,
            IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(new GenerarLayoutCorridaCommand(id, banco ?? "BBVA", tipoPago ?? TipoPagoLayout.NacionalSpei), ct);
            return Results.File(
                System.Text.Encoding.UTF8.GetBytes(r.Contenido),
                contentType: "text/plain; charset=utf-8",
                fileDownloadName: r.NombreArchivo);
        })
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasEjecutar)
            .WithName("DescargarLayoutCorridaPago")
            .Produces(StatusCodes.Status200OK, contentType: "text/plain")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        corridas.MapPost("/{id:guid}/ejecucion", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            [FromBody] EjecucionBody body, IMediator mediator, DocumentoSucursalScope scope, CancellationToken ct) =>
        {
            if (v is not int version) return SinVersion();
            await VerificarSucursalesAsync(scope, body.Pagos?.SelectMany(p => p.Facturas ?? []), ct);
            return Results.Ok(await mediator.Send(new RegistrarEjecucionCorridaCommand(id, body.Pagos ?? [], version), ct));
        })
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasEjecutar)
            .WithName("RegistrarEjecucionCorridaPago").Produces<CorridaResponse>()
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        corridas.MapPost("/{id:guid}/cerrar", async (Guid id, [FromHeader(Name = "X-Expected-Version")] int? v,
            IMediator mediator, CancellationToken ct) =>
            v is not int version ? SinVersion() : Results.Ok(await mediator.Send(new CerrarCorridaCommand(id, version), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCorridasEjecutar)
            .WithName("CerrarCorridaPago").Produces<CorridaResponse>();

        corridas.MapGet("/politica-firmas", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ObtenerPoliticaFirmasQuery(), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaPasivosVer)
            .WithName("ObtenerPoliticaFirmasCorrida").Produces<IReadOnlyList<EscalonFirmaResponse>>();

        corridas.MapPut("/politica-firmas/{moneda}", async (string moneda, [FromBody] PoliticaBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GuardarPoliticaFirmasCommand(moneda, body.Escalones), ct)))
            .RequireAuthorization(P + PermisosCanonicos.TesoreriaCuentasAdministrar)
            .WithName("GuardarPoliticaFirmasCorrida").Produces<IReadOnlyList<EscalonFirmaResponse>>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task VerificarSucursalesAsync(DocumentoSucursalScope scope, IEnumerable<Guid>? facturas, CancellationToken ct)
    {
        foreach (var f in facturas ?? [])
            await scope.VerificarAsync("factura_proveedor", f, PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales, ct);
    }

    private static IResult SinVersion() =>
        Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

    public sealed record AgregarPasivosBody(IReadOnlyList<Guid> Facturas);
    public sealed record MotivoBody(string Motivo);
    public sealed record EjecucionBody(IReadOnlyList<PagoCorridaItem>? Pagos);
    public sealed record PoliticaBody(IReadOnlyList<EscalonFirmaItem> Escalones);
}
