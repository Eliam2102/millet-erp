using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.CuentasPorCobrar.Application.Anticipos;
using Millet.CuentasPorCobrar.Application.Cartera;
using Millet.CuentasPorCobrar.Application.Reportes.AntiguedadSaldos;
using Millet.CuentasPorCobrar.Application.Reportes.Comun;
using Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints de cartera (CXC-PR3, §11 del 01-diseño): saldo neto 13-K
/// derivado de la proyección <c>factura_cartera</c> y saldos de anticipo
/// del cliente vía <c>IFacturacionAnticiposReadPort</c>. La antigüedad y
/// el estado de cuenta completos (ADR-0036) entran en CXC-PR6.
/// </summary>
public static class CarteraEndpoints
{
    public static IEndpointRouteBuilder MapCarteraEndpoints(this IEndpointRouteBuilder app)
    {
        var cartera = app
            .MapGroup("/api/v1/cuentas-por-cobrar")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        cartera.MapGet("/cartera/saldo-neto/{clienteId:guid}", async (
            Guid clienteId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new SaldoNetoClienteQuery(clienteId), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ObtenerSaldoNetoCliente")
        .Produces<SaldoNetoClienteResponse>(StatusCodes.Status200OK);

        // CXC-PR6: reportes ADR-0036 (JSON estructurado → <ReporteShell>).
        cartera.MapGet("/cartera/antiguedad", async (
            [FromQuery] DateOnly? fechaCorte,
            [FromQuery] Guid? clienteId,
            [FromQuery] string? moneda,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new AntiguedadSaldosQuery(fechaCorte, clienteId, moneda), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ObtenerAntiguedadSaldos")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        cartera.MapGet("/cartera/estado-cuenta/{clienteId:guid}", async (
            Guid clienteId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new EstadoCuentaClienteQuery(clienteId), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ObtenerEstadoCuentaCliente")
        .Produces<ReporteJsonResponse>(StatusCodes.Status200OK);

        // Facturas vivas del cliente para el matching depósito↔facturas
        // (CXC-FE-PR6, 05-frontend-diseno §4.1). Gate del flujo que las
        // consume: aplicacion-pago.proponer.
        cartera.MapGet("/cartera/facturas-abiertas", async (
            [FromQuery] Guid clienteId,
            [FromQuery] string? moneda,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new FacturasAbiertasClienteQuery(clienteId, moneda), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer)
        .WithName("ListarFacturasAbiertasCliente")
        .Produces<IReadOnlyList<FacturaAbiertaDto>>(StatusCodes.Status200OK);

        cartera.MapGet("/anticipos", async (
            [FromQuery] Guid clienteId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new AnticiposClienteQuery(clienteId), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarAnticiposCliente")
        .Produces<IReadOnlyList<AnticipoSaldoClienteDto>>(StatusCodes.Status200OK);

        return app;
    }
}
