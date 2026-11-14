using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Conciliacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// M-314 · conciliación fiscal: resolver una incidencia (el SAT vs el estado del comprobante) con nueva consulta y soporte, y cierre de
/// folios por serie y mes. <c>/api/v1/facturacion/conciliacion</c>. Sin permisos nuevos (supuesto a validar): leer = reportes; resolver y
/// cerrar = autorizar cancelaciones; reabrir = administrar caja.
/// </summary>
public static class ConciliacionFiscalEndpoints
{
    public static IEndpointRouteBuilder MapFacturacionConciliacionFiscalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/facturacion/conciliacion").WithTags("Facturacion");
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReportesLeer;
        var resolver = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCancelacionesAutorizar;
        var reabrir = PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaAdministrar;

        group.MapPost("/incidencias/{comprobanteId:guid}/resolver", async (Guid comprobanteId, [FromBody] ResolverIncidenciaFiscalRequest body,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ResolverIncidenciaFiscalCommand(comprobanteId, body.Soporte, body.Nota), ct)))
            .RequireAuthorization(resolver)
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("ResolverIncidenciaFiscal")
            .WithSummary("Vuelve a consultar el estatus del UUID y resuelve la incidencia si ya no hay diferencia; exige soporte")
            .Produces<ResolucionIncidenciaFiscalResponse>(StatusCodes.Status200OK);

        group.MapGet("/incidencias/resoluciones", async ([FromQuery] Guid? comprobanteId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ListarResolucionesIncidenciaFiscalQuery(comprobanteId), ct)))
            .RequireAuthorization(leer)
            .WithName("ListarResolucionesIncidenciaFiscal");

        group.MapGet("/cierre-folios/{anio:int}/{mes:int}", async (int anio, int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConsultarCierreFoliosQuery(anio, mes), ct)))
            .RequireAuthorization(leer)
            .WithName("ConsultarCierreFolios")
            .WithSummary("Folios del mes por serie (usados, cancelados, descartados, pendientes y huecos) y el estado de su cierre")
            .Produces<CierreFoliosResponse>(StatusCodes.Status200OK);

        group.MapPost("/cierre-folios/{anio:int}/{mes:int}/cerrar", async (int anio, int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CerrarFoliosPeriodoCommand(anio, mes), ct)))
            .RequireAuthorization(resolver)
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("CerrarFoliosPeriodo")
            .WithSummary("Cierra los folios del mes: sin huecos ni pendientes; guarda el reporte final")
            .Produces<CierreFoliosResponse>(StatusCodes.Status200OK);

        group.MapPost("/cierre-folios/{cierreId:guid}/reabrir", async (Guid cierreId, [FromBody] ReabrirCierreFoliosRequest body,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReabrirCierreFoliosCommand(cierreId, body.Motivo), ct)))
            .RequireAuthorization(reabrir)
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .WithName("ReabrirCierreFolios")
            .WithSummary("Reabre un cierre de folios (permiso propio); queda con motivo, quién y cuándo")
            .Produces<CierreFoliosSerieResponse>(StatusCodes.Status200OK);

        return app;
    }

    public sealed record ResolverIncidenciaFiscalRequest(string? Soporte, string? Nota);
    public sealed record ReabrirCierreFoliosRequest(string? Motivo);
}
