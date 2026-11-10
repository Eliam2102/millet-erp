using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Contabilidad.Application.ContabilidadElectronica;
using Millet.Contabilidad.Application.Ports;
using Millet.Contabilidad.Application.Reportes;
using Millet.Contabilidad.Domain;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// C1.7 (F1-CON-06 / CON-07): balanza de comprobación, mayor, auxiliar y libro diario bajo <c>/api/v1/contabilidad/reportes</c>.
/// Solo lectura con el permiso de lectura de pólizas (no se crean permisos). Contabilidad es corporativa: el filtro de sucursal es
/// opcional y no se limita por las sucursales del usuario (mismo criterio que K10.5 y la bandeja de pólizas). Forma ADR-0036.
/// </summary>
public static class ContabilidadReportesEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var g = app.MapGroup("/api/v1/contabilidad/reportes").WithTags("Contabilidad · Reportes").RequireAuthorization();

        g.MapGet("/balanza", async (
            [FromQuery] int anio, [FromQuery] int periodo, [FromQuery] bool? acumulada, [FromQuery] short? nivel, [FromQuery] Guid? sucursalId,
            [FromQuery] bool? soloConSaldo, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new BalanzaQuery(anio, periodo, acumulada ?? false, nivel, sucursalId, soloConSaldo ?? true), ct)))
        .RequireAuthorization(leer).WithName("BalanzaComprobacion")
        .WithSummary("Balanza de comprobación del periodo o acumulada del ejercicio, por nivel y sucursal")
        .WithDescription("Saldo inicial = todo lo anterior al primer periodo del rango. acumulada=true suma del periodo 1 al indicado. nivel (1 a 9) acumula cada cuenta en su ancestro de ese nivel. Totales sobre todas las cuentas.")
        .Produces<BalanzaResponse>().ProducesValidationProblem();

        g.MapGet("/mayor", async (
            [FromQuery] Guid cuentaId, [FromQuery] int anio, [FromQuery] int desde, [FromQuery] int? hasta, [FromQuery] Guid? sucursalId,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new MayorQuery(cuentaId, anio, desde, hasta ?? desde, sucursalId), ct)))
        .RequireAuthorization(leer).WithName("MayorCuentaContable")
        .WithSummary("Mayor de una cuenta: saldo inicial, movimientos con saldo corrido y saldo final (incluye descendientes)")
        .Produces<MayorResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/auxiliar", async (
            [FromQuery] Guid cuentaId, [FromQuery] TipoAuxiliar tipo, [FromQuery] int anio, [FromQuery] int desde, [FromQuery] int? hasta,
            [FromQuery] Guid? sucursalId, [FromQuery] bool? soloConSaldo, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AuxiliarQuery(cuentaId, tipo, anio, desde, hasta ?? desde, sucursalId, soloConSaldo ?? true), ct)))
        .RequireAuthorization(leer).WithName("AuxiliarCuentaContable")
        .WithSummary("Auxiliar de una cuenta por cliente, proveedor o banco, conciliado contra el mayor")
        .Produces<AuxiliarResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapGet("/diario", async (
            [FromQuery] int anio, [FromQuery] int desde, [FromQuery] int? hasta, [FromQuery] Guid? sucursalId, [FromQuery] Guid? tipoOperacionId,
            [FromQuery] OrigenPoliza? origen, [FromQuery] Guid? cuentaId, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DiarioQuery(anio, desde, hasta ?? desde, sucursalId, tipoOperacionId, origen, cuentaId,
                offset ?? 0, limit ?? 100), ct)))
        .RequireAuthorization(leer).WithName("LibroDiarioContable")
        .WithSummary("Libro diario: pólizas del rango con sus partidas, paginado por póliza (limit máximo 1000)")
        .Produces<DiarioResponse>().ProducesValidationProblem();

        // TES-09 (86e3jvvj4): diferencias cambiarias realizadas del periodo (pagos a proveedor y cobros de cliente, CON-11).
        // Los parámetros llegan como texto y se validan aquí para que un valor mal formado responda 400 con mensaje en español
        // (el binding de ASP.NET responde en inglés).
        g.MapGet("/diferencias-cambiarias", async (
            [FromQuery] string? desde, [FromQuery] string? hasta, [FromQuery] string? sucursalId, [FromQuery] string? origen,
            IMediator mediator, CancellationToken ct) =>
        {
            var (inicio, fin, sucursal, origenFiltro) = FiltrosDiferenciasCambiarias.Leer(desde, hasta, sucursalId, origen);
            return Results.Ok(await mediator.Send(new DiferenciasCambiariasQuery(inicio, fin, sucursal, origenFiltro), ct));
        })
        .RequireAuthorization(leer).WithName("DiferenciasCambiariasRealizadas")
        .WithSummary("Diferencias cambiarias realizadas del periodo: una fila por pago o cobro en moneda extranjera con TC de factura, TC de pago y pérdida o utilidad")
        .WithDescription("hasta por omisión = hoy (zona de operación); desde por omisión = primer día del mes de hasta. origen = Proveedores | Clientes (opcional). Periodo máximo de un año. Avisos lista los roles cambiarios sin cuenta.")
        .Produces<DiferenciasCambiariasResponse>().ProducesValidationProblem();

        // K10.8 (F1-CON-13): contabilidad electrónica del SAT (anexo 24, versión 1.3). La revisión lista lo que impide generar
        // (K10.8-a: cuentas sin código agrupador) y los avisos; la generación devuelve el .zip con el XML validado contra el esquema.
        var ce = app.MapGroup("/api/v1/contabilidad/contabilidad-electronica").WithTags("Contabilidad · Contabilidad electrónica")
            .RequireAuthorization(leer);

        ce.MapGet("/revision", async ([FromQuery] int anio, [FromQuery] int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RevisionContabilidadElectronicaQuery(anio, mes), ct)))
        .WithName("RevisionContabilidadElectronica")
        .WithSummary("Revisión antes de generar: RFC, estado del periodo, cuentas sin código agrupador SAT y CFDI de las pólizas")
        .Produces<RevisionContabilidadElectronicaResponse>().ProducesValidationProblem();

        ce.MapGet("/{tipo}", async (string tipo, [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string? tipoEnvio,
            [FromQuery] string? fechaModificacion, [FromQuery] string? solicitud, [FromQuery] string? numOrden, [FromQuery] string? numTramite,
            [FromQuery] string? formato, IMediator mediator, CancellationToken ct) =>
        {
            var query = ParametrosContabilidadElectronica.Leer(tipo, anio, mes, tipoEnvio, fechaModificacion, solicitud, numOrden, numTramite, formato);
            var archivo = await mediator.Send(query, ct);
            return Results.File(archivo.Contenido, archivo.ContentType, archivo.NombreArchivo);
        })
        .WithName("GenerarContabilidadElectronica")
        .WithSummary("XML del catálogo (catalogo), la balanza (balanza) o las pólizas (polizas) del mes, validado contra el esquema del SAT")
        .WithDescription("Nombre del SAT: RFC + año + mes + CT/BN/BC/PL. formato=zip (por omisión, el que se sube al buzón tributario) o xml. Balanza: mes 13 = cierre; tipoEnvio=C exige fechaModificacion. Pólizas: solicitud AF/FC con numOrden o DE/CO con numTramite.")
        .Produces<byte[]>(StatusCodes.Status200OK, "application/zip", "application/xml").ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
