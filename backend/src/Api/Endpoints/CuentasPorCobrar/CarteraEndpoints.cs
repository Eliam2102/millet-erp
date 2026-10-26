using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
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

        // F1-CXC-09 · C7.8: pagos que bloquean el estado de cuenta (CA7.14).
        cartera.MapGet("/cartera/estado-cuenta/{clienteId:guid}/pendientes", async (
            Guid clienteId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new PendientesEstadoCuentaQuery(clienteId), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarPendientesEstadoCuenta")
        .Produces<PendientesEstadoCuentaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // C7.8: historial de envíos del estado de cuenta, más reciente primero.
        cartera.MapGet("/cartera/estado-cuenta/{clienteId:guid}/envios", async (
            Guid clienteId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new ListarEnviosEstadoCuentaQuery(clienteId), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarEnviosEstadoCuenta")
        .Produces<IReadOnlyList<EstadoCuentaEnvioResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        // C7.8: emitir y enviar. Multipart: pdf (generado en el navegador,
        // ADR-0036) + destinatario + mensaje opcional. El bloqueo se recalcula
        // en servidor: con pagos sin REP responde 422 sin registrar nada.
        cartera.MapPost("/cartera/estado-cuenta/{clienteId:guid}/envios", async (
            Guid clienteId,
            [FromForm] string? destinatario,
            [FromForm] string? mensaje,
            IFormFile? pdf,
            IMediator mediator,
            DocumentoSucursalScope scope,
            CancellationToken cancellationToken) =>
        {
            await scope.VerificarAsync("cliente_cartera", clienteId,
                PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales, cancellationToken);

            byte[] contenido = [];
            if (pdf is not null && pdf.Length is > 0 and <= EnviarEstadoCuentaValidator.TamanoMaximoBytes)
            {
                using var buffer = new MemoryStream();
                await pdf.CopyToAsync(buffer, cancellationToken);
                contenido = buffer.ToArray();
            }
            else if (pdf is not null && pdf.Length > EnviarEstadoCuentaValidator.TamanoMaximoBytes)
            {
                // Se rechaza sin leer el archivo completo a memoria.
                contenido = new byte[EnviarEstadoCuentaValidator.TamanoMaximoBytes + 1];
            }

            var response = await mediator.Send(new EnviarEstadoCuentaCommand(
                clienteId,
                destinatario ?? string.Empty,
                mensaje,
                string.IsNullOrWhiteSpace(pdf?.FileName) ? "estado-cuenta.pdf" : Path.GetFileName(pdf.FileName),
                pdf?.ContentType ?? string.Empty,
                contenido), cancellationToken);
            return Results.Created(
                $"/api/v1/cuentas-por-cobrar/cartera/estado-cuenta/{clienteId}/envios/{response.Id}", response);
        })
        .DisableAntiforgery() // Multipart en API REST con JWT bearer.
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("EnviarEstadoCuentaCliente")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<EstadoCuentaEnvioResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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
