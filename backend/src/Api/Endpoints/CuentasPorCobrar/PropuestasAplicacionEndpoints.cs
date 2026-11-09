using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.AplicacionPagos;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.AplicacionPagos;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints de propuestas de aplicación de pago (CXC-PR7, §11 del
/// 01-diseño). CxC propone (permiso <c>aplicacion-pago.proponer</c>);
/// Tesorería confirma o rechaza y CxC consume la resolución por eventos.
/// </summary>
public static class PropuestasAplicacionEndpoints
{
    /// <summary>
    /// Ver la bandeja y el detalle: quien propone (CxC) y quien autoriza el ajuste o confirma (Ingresos,
    /// <c>aplicacion-pago.confirmar</c>). Sin leer la propuesta, Ingresos no puede autorizar su ajuste (403 en la bandeja).
    /// </summary>
    public const string LeerPropuestas = PermissionPolicyProvider.Prefix
        + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer + "|" + PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar;

    public static IEndpointRouteBuilder MapPropuestasAplicacionEndpoints(this IEndpointRouteBuilder app)
    {
        var propuestas = app
            .MapGroup("/api/v1/cuentas-por-cobrar/propuestas-aplicacion")
            .WithDocumentoSucursalScope("propuesta_cxc", "cuentas_por_cobrar.cartera", "id")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        // Tolerancias no fiscales por moneda (CXC-FE-PR6): el FE las lee
        // para habilitar el aviso de ajuste en el matching sin duplicar la
        // config (provisional: MXN=1000, USD=50 — pendiente gate fiscal).
        propuestas.MapGet("/tolerancias", (
            Microsoft.Extensions.Options.IOptions<Millet.CuentasPorCobrar.Application.AplicacionPagos.AplicacionPagosOptions> options) =>
            Results.Ok(options.Value.ToleranciaNoFiscal))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer)
        .WithName("ObtenerToleranciasNoFiscales")
        .Produces<Dictionary<string, decimal>>(StatusCodes.Status200OK);

        propuestas.MapGet("/", async (
            [FromQuery] EstadoPropuestaAplicacion? estado,
            [FromQuery] Guid? clienteId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarPropuestasAplicacionQuery(estado, clienteId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(LeerPropuestas)
        .WithName("ListarPropuestasAplicacion")
        .Produces<PagedResponse<PropuestaAplicacionResponse>>(StatusCodes.Status200OK);

        // C7.4 · CA7.9: depósitos de cliente sin identificar (Tesorería) para que CxC
        // los identifique cuando llegue el remittance. Alcance por sucursal (ADR-0051)
        // en el handler (ISucursalScopedQuery).
        propuestas.MapGet("/depositos-por-identificar", async (
            IMediator mediator,
            CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new DepositosPorIdentificarQuery(), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer)
        .WithName("ListarDepositosPorIdentificar")
        .Produces<IReadOnlyList<DepositoPorIdentificarResponse>>(StatusCodes.Status200OK);

        propuestas.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerPropuestaAplicacionQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(LeerPropuestas)
        .WithName("ObtenerPropuestaAplicacion")
        .Produces<PropuestaAplicacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        propuestas.MapPost("/", async (
            [FromBody] CrearPropuestaAplicacionCommand command,
            CuentasPorCobrarDbContext scopeDb,
            DocumentoSucursalScope scope,
            Millet.CuentasPorCobrar.Domain.Ports.Tesoreria.IDepositosPorIdentificarReadPort depositos,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            foreach (var linea in command.Facturas ?? [])
            {
                var facturaId = await scopeDb.FacturasCartera.AsNoTracking()
                    .Where(x => x.Uuid == linea.FacturaUuid).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
                await scope.VerificarAsync("factura_cartera", facturaId ?? Guid.Empty,
                    PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales, cancellationToken);
            }
            // C7.4 (ADR-0051): identificar un depósito exige alcance sobre su sucursal;
            // el depósito sin sucursal solo lo identifica el corporativo.
            if (command.MovimientoBancarioId is Guid movimientoId
                && await depositos.ObtenerAsync(movimientoId, cancellationToken) is { } deposito)
                await scope.VerificarSucursalAsync(deposito.SucursalId,
                    PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales, cancellationToken);
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-cobrar/propuestas-aplicacion/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer)
        .WithName("CrearPropuestaAplicacion")
        .Produces<PropuestaAplicacionResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // CXC-06 (86e3qddef): el ajuste documentado de un cobro corto lo autoriza o rechaza otra persona (no quien propuso).
        // Supuesto «a validar»: usa el permiso existente aplicacion-pago.confirmar (Ingresos), sin permiso nuevo.
        propuestas.MapPost("/{id:guid}/autorizar-ajuste", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new AutorizarAjustePropuestaCommand(id, v), cancellationToken));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar)
        .WithName("AutorizarAjustePropuestaAplicacion")
        .WithSummary("Autoriza el ajuste documentado de un cobro corto (CXC-06)")
        .Produces<PropuestaAplicacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        propuestas.MapPost("/{id:guid}/rechazar-ajuste", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] RechazarAjusteBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new RechazarAjustePropuestaCommand(id, v, body.Motivo), cancellationToken));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar)
        .WithName("RechazarAjustePropuestaAplicacion")
        .WithSummary("Rechaza el ajuste de un cobro corto: la propuesta queda rechazada (CXC-06)")
        .Produces<PropuestaAplicacionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record RechazarAjusteBody(string Motivo);
}
