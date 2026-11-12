using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Contabilidad.Application.Iva;
using Millet.Contabilidad.Application.Polizas;
using Millet.Identidad.Domain;
using Helpers = Millet.Api.Endpoints.CentrosCosto.CentrosCostoCatalogoEndpoints;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// K10.7: liquidación mensual de IVA bajo <c>/api/v1/contabilidad/iva</c>. Lectura con el permiso de lectura de pólizas y el borrador de
/// póliza con el de captura (no se crean permisos). Contabilidad es corporativa: el reporte no se limita por sucursal.
/// </summary>
public static class ContabilidadIvaEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadIvaEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var capturar = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasCapturar;
        var g = app.MapGroup("/api/v1/contabilidad/iva").WithTags("Contabilidad · IVA").RequireAuthorization();

        g.MapGet("/liquidacion", async ([FromQuery] int anio, [FromQuery] int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new LiquidacionIvaQuery(anio, mes), ct)))
        .RequireAuthorization(leer).WithName("LiquidacionIvaMensual")
        .WithSummary("IVA trasladado cobrado, acreditable pagado, retenciones e IVA a cargo o a favor del mes, con cuadre contra cuentas y detalle por documento")
        .Produces<LiquidacionIvaResponse>().ProducesValidationProblem();

        g.MapPost("/liquidacion/poliza", async (GenerarPolizaLiquidacionIvaCommand b, HttpResponse response, IMediator mediator, CancellationToken ct) =>
        {
            var r = await mediator.Send(b, ct);
            Helpers.SetEtag(response, r.Version);
            return Results.Created($"/api/v1/contabilidad/polizas/{r.Id}", r);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("GenerarPolizaLiquidacionIva")
        .WithSummary("Borrador de la póliza de liquidación de IVA del mes con las cuentas de los roles (el contador la revisa y autoriza)")
        .Produces<PolizaResponse>(StatusCodes.Status201Created).ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // M-311: papel de trabajo del mes (foto inmutable, acuse y diferencias). Reusa los permisos de pólizas: leer / capturar.
        g.MapGet("/papel-trabajo", async ([FromQuery] int anio, [FromQuery] int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new PapelTrabajoIvaQuery(anio, mes), ct)))
        .RequireAuthorization(leer).WithName("PapelTrabajoIva")
        .WithSummary("Foto guardada de la liquidación de IVA del mes, su acuse y las diferencias fiscales")
        .Produces<PapelTrabajoIvaResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/papel-trabajo/detectar", async ([FromQuery] int anio, [FromQuery] int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DetectarDiferenciasIvaCommand(anio, mes), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("DetectarDiferenciasIva")
        .WithSummary("Compara la foto del mes con lo que el mes muestra hoy y registra las diferencias pendientes")
        .Produces<PapelTrabajoIvaResponse>().ProducesProblem(StatusCodes.Status404NotFound);

        g.MapPost("/papel-trabajo/acuse", async ([FromQuery] int anio, [FromQuery] int mes, IFormFile archivo, IMediator mediator, CancellationToken ct) =>
        {
            await using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms, ct);
            return Results.Ok(await mediator.Send(new AdjuntarAcuseIvaCommand(anio, mes, archivo.FileName, archivo.ContentType, ms.ToArray()), ct));
        })
        .DisableAntiforgery().WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("AdjuntarAcuseIva")
        .WithSummary("Adjunta el acuse de la declaración de IVA (uno por mes, no se reemplaza)")
        .Produces<AcuseIvaResponse>().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapPost("/papel-trabajo/diferencias/{id:guid}/ligar-ajuste", async (Guid id, LigarAjusteIvaRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new LigarAjusteDiferenciaIvaCommand(id, b.AjustePolizaId), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(capturar).WithName("LigarAjusteDiferenciaIva")
        .WithSummary("Cierra una diferencia fiscal ligándola a la póliza de ajuste que la corrige")
        .Produces<DiferenciaFiscalIvaResponse>().ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // M-209 (CA4): recalcular el IVA de una factura de proveedor contra su XML. Sin «aplicar», solo la vista previa de la diferencia;
        // con él, póliza de ajuste ligada al documento (la original no se anula) y saldo de IVA corregido.
        var corregir = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadIvaCorregirDocumento;
        g.MapPost("/documentos/{saldoId:guid}/recalcular", async (Guid saldoId, RecalcularIvaRequest b, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RecalcularIvaDocumentoCommand(saldoId, b.Motivo, b.Aplicar), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(corregir).WithName("RecalcularIvaDocumento")
        .WithSummary("Recalcula el IVA de una factura de proveedor contra su XML; con aplicar=true genera la póliza de ajuste")
        .Produces<RecalculoIvaResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return app;
    }
}

/// <summary>M-209 (CA4): motivo (obligatorio al aplicar) y si se aplica o solo se previsualiza.</summary>
public sealed record RecalcularIvaRequest(string? Motivo, bool Aplicar);

/// <summary>M-311: póliza de ajuste que corrige la diferencia.</summary>
public sealed record LigarAjusteIvaRequest(Guid AjustePolizaId);
