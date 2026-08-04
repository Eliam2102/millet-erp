using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.Pasivos;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP de la bandeja de pasivos pendientes de pago (TES-PR3,
/// §11 del 01-diseño). Solo lectura: la proyección la escriben el
/// listener de CxP y las aplicaciones de pago (PR-4); las mutaciones del
/// flujo (pagar, solicitar cancelación) llegan con PR-4.
/// </summary>
public static class PasivosEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaPasivosEndpoints(this IEndpointRouteBuilder app)
    {
        var pasivos = app
            .MapGroup("/api/v1/tesoreria/pasivos-pendientes")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        pasivos.MapGet("/", async (
            [FromQuery] Guid? proveedorId,
            [FromQuery] string? moneda,
            [FromQuery] DateOnly? venceDesde,
            [FromQuery] DateOnly? venceHasta,
            [FromQuery] decimal? montoMinimo,
            [FromQuery] decimal? montoMaximo,
            [FromQuery] bool? soloConSaldo,
            [FromQuery] string? tipoBeneficiario,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new BandejaPasivosPendientesQuery(
                    proveedorId, moneda, venceDesde, venceHasta,
                    montoMinimo, montoMaximo, soloConSaldo ?? true,
                    tipoBeneficiario,
                    offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPasivosVer)
        .WithName("ListarPasivosPendientes")
        .Produces<PagedResponse<PasivoPendienteResponse>>(StatusCodes.Status200OK);

        // TES-PR4: solicitud hacia CxP (cancelacion-pasivo.solicitada.v1);
        // CxP responde con EnviarARevision — sin mutación local.
        pasivos.MapPost("/{facturaProveedorId:guid}/solicitar-cancelacion", async (
            Guid facturaProveedorId,
            [FromBody] SolicitarCancelacionBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new SolicitarCancelacionPasivoCommand(facturaProveedorId, body.Motivo),
                cancellationToken);
            return Results.Accepted();
        })
        .WithMetadata(new Millet.Api.Web.RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPasivosSolicitarCancelacion)
        .WithName("SolicitarCancelacionPasivo")
        .Produces(StatusCodes.Status202Accepted)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    public sealed record SolicitarCancelacionBody(string Motivo);
}
