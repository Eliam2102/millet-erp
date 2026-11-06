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
        return app;
    }
}
