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
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer)
        .WithName("ListarPropuestasAplicacion")
        .Produces<PagedResponse<PropuestaAplicacionResponse>>(StatusCodes.Status200OK);

        propuestas.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new ObtenerPropuestaAplicacionQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer)
        .WithName("ObtenerPropuestaAplicacion")
        .Produces<PropuestaAplicacionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        propuestas.MapPost("/", async (
            [FromBody] CrearPropuestaAplicacionCommand command,
            CuentasPorCobrarDbContext scopeDb,
            DocumentoSucursalScope scope,
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

        return app;
    }

}
