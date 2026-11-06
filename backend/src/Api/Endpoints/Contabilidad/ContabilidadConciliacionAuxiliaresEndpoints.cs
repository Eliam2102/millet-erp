using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Contabilidad.Application.ConciliacionAuxiliares;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.Endpoints.Contabilidad;

/// <summary>
/// F1-CON-09 (86e3jvvm7): conciliación de auxiliares contra cuentas de control, bajo
/// <c>/api/v1/contabilidad/reportes/conciliacion-auxiliares</c>. Solo lectura con el permiso de lectura de pólizas (como la cuenta
/// puente; no se crean permisos). Contabilidad es corporativa: la sucursal es un filtro opcional.
/// </summary>
public static class ContabilidadConciliacionAuxiliaresEndpoints
{
    public static IEndpointRouteBuilder MapContabilidadConciliacionAuxiliaresEndpoints(this IEndpointRouteBuilder app)
    {
        var leer = PermissionPolicyProvider.Prefix + PermisosCanonicos.ContabilidadPolizasLeer;
        var g = app.MapGroup("/api/v1/contabilidad/reportes/conciliacion-auxiliares")
            .WithTags("Contabilidad · Conciliación de auxiliares").RequireAuthorization();

        g.MapGet("/", async ([FromQuery] DateOnly? corte, [FromQuery] Guid? sucursalId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConciliacionAuxiliaresQuery(corte ?? HoyContable(), sucursalId), ct)))
        .RequireAuthorization(leer).WithName("ConciliacionAuxiliares")
        .WithSummary("Clientes, proveedores y bancos: saldo de la cuenta de control contra el total de su módulo al corte")
        .WithDescription("corte por omisión = hoy (zona de operación). Estado por control: Cuadra, Diferencia, SinCuenta (no hay cuenta de control ni rol configurado) o NoComparable.")
        .Produces<ConciliacionAuxiliaresResponse>().ProducesValidationProblem();

        g.MapGet("/{control}", async (string control, [FromQuery] DateOnly? corte, [FromQuery] Guid? sucursalId,
            IMediator mediator, CancellationToken ct) =>
        {
            if (!Enum.TryParse<ControlConciliacion>(control, ignoreCase: true, out var c) || !Enum.IsDefined(c))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                    { ["control"] = ["Control no válido: usa proveedores o bancos (el de clientes está en Cuentas por cobrar)."] });
            return Results.Ok(await mediator.Send(new ConciliacionAuxiliarDetalleQuery(c, corte ?? HoyContable(), sucursalId), ct));
        })
        .RequireAuthorization(leer).WithName("ConciliacionAuxiliarDetalle")
        .WithSummary("Diferencias por proveedor, documento o cuenta bancaria de un control al corte (ADR-0036)")
        .Produces<ConciliacionAuxiliarDetalleResponse>().ProducesValidationProblem();
        return app;
    }

    private static DateOnly HoyContable() => FechaContable.HoyLocal(DateTimeOffset.UtcNow);
}
