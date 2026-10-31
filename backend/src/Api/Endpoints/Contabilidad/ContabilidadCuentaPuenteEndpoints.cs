using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Contabilidad.Application.CuentaPuente;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// K10.5: reporte de conciliación de la cuenta puente recepción–factura por OC, bajo <c>/api/v1/contabilidad/reportes/cuenta-puente</c>.
/// Solo lectura con el permiso de lectura de pólizas (no se crean permisos). Contabilidad es corporativa: el filtro de sucursal es
/// opcional y no se limita por las sucursales del usuario (mismo criterio que la bandeja de pólizas y los saldos).
/// </summary>
public static class ContabilidadCuentaPuenteEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadCuentaPuenteEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var g = app.MapGroup("/api/v1/contabilidad/reportes/cuenta-puente").WithTags("Contabilidad · Cuenta puente").RequireAuthorization();

        g.MapGet("/", async (
            [FromQuery] DateOnly? corte, [FromQuery] Guid? sucursalId, [FromQuery] Guid? proveedorId, [FromQuery] bool? soloConSaldo,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConciliacionPuenteQuery(corte ?? HoyContable(), sucursalId, proveedorId, soloConSaldo ?? true), ct)))
        .RequireAuthorization(leer).WithName("ConciliacionCuentaPuente")
        .WithSummary("Saldo de la cuenta puente por OC al corte (naturaleza, antigüedad y tramo) conciliado contra la balanza")
        .WithDescription("corte por omisión = hoy (zona de operación). soloConSaldo por omisión = true; no cambia los totales. Con proveedorId no se concilia contra balanza (saldoBalanza, partidasSinOc y cuadra en null).")
        .Produces<ConciliacionPuenteResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        g.MapGet("/{ordenCompraId:guid}/movimientos", async (Guid ordenCompraId, [FromQuery] DateOnly? corte, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new MovimientosPuenteOcQuery(ordenCompraId, corte ?? HoyContable()), ct)))
        .RequireAuthorization(leer).WithName("MovimientosCuentaPuenteOc")
        .WithSummary("Movimientos de la cuenta puente de una OC al corte, con saldo acumulado")
        .Produces<MovimientosPuenteOcResponse>().ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        return app;
    }

    private static DateOnly HoyContable() => FechaContable.HoyLocal(DateTimeOffset.UtcNow);
}
