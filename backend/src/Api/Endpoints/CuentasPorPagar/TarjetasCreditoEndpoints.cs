using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.TarjetaCredito.Disputas;
using Millet.CuentasPorPagar.Application.TarjetaCredito.EstadosCuenta;
using Millet.CuentasPorPagar.Application.TarjetaCredito.Movimientos;
using Millet.CuentasPorPagar.Application.TarjetaCredito.Tarjetas;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del sub-módulo de Tarjetas de Crédito empresariales
/// (F7-PR4). Cubre el master de tarjetas + usuarios autorizados + la
/// captura de movimientos en flujos A (con CFDI) y B (sin CFDI). El
/// estado de cuenta + parser + conciliación entran en F7-PR5; el cierre
/// + casos especiales (refund, anualidad) entran en F7-PR6.
/// </summary>
public static class TarjetasCreditoEndpoints
{
    public static IEndpointRouteBuilder MapTarjetasCreditoEndpoints(this IEndpointRouteBuilder app)
    {
        // -------- Master: Tarjetas --------
        var tarjetas = app
            .MapGroup("/api/v1/cuentas-por-pagar/tarjetas")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        tarjetas.MapGet("/", async (
            [FromQuery] EstadoTarjeta? estado,
            [FromQuery] Guid? titularId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarTarjetasQuery(estado, titularId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcLeer)
        .WithName("ListarTarjetas")
        .Produces<PagedResponse<TarjetaResponse>>(StatusCodes.Status200OK);

        tarjetas.MapPost("/", async (
            [FromBody] CrearTarjetaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/tarjetas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("CrearTarjeta")
        .Produces<TarjetaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        tarjetas.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ActualizarTarjetaBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ActualizarTarjetaCommand(id, v, body.NombreAlias, body.LimiteCreditoMxn, body.DiaCorte, body.DiaLimitePago),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("ActualizarTarjeta")
        .Produces<TarjetaResponse>(StatusCodes.Status200OK);

        tarjetas.MapPost("/{id:guid}/bloquear", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] BloquearBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new BloquearTarjetaCommand(id, v, body.Motivo, body.Fecha), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("BloquearTarjeta")
        .Produces<TarjetaResponse>(StatusCodes.Status200OK);

        tarjetas.MapPost("/{id:guid}/reactivar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new ReactivarTarjetaCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("ReactivarTarjeta")
        .Produces<TarjetaResponse>(StatusCodes.Status200OK);

        tarjetas.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] CancelarBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(new CancelarTarjetaCommand(id, v, body.Fecha), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("CancelarTarjeta")
        .Produces<TarjetaResponse>(StatusCodes.Status200OK);

        // Usuarios autorizados
        tarjetas.MapPost("/{id:guid}/usuarios", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] AgregarUsuarioBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new AgregarUsuarioAutorizadoCommand(id, v, body.EmpleadoId, body.VigenciaDesde, body.VigenciaHasta, body.MontoMaxMensualMxn),
                cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/tarjetas/{id}/usuarios/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("AgregarUsuarioAutorizadoTarjeta")
        .Produces<UsuarioAutorizadoResponse>(StatusCodes.Status201Created);

        tarjetas.MapPost("/{id:guid}/usuarios/{usuarioId:guid}/cerrar", async (
            Guid id,
            Guid usuarioId,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] CerrarUsuarioBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            await mediator.Send(new CerrarUsuarioAutorizadoCommand(id, v, usuarioId, body.Fecha), cancellationToken);
            return Results.NoContent();
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcAdministrar)
        .WithName("CerrarUsuarioAutorizadoTarjeta")
        .Produces(StatusCodes.Status204NoContent);

        // -------- Movimientos --------
        var movimientos = app
            .MapGroup("/api/v1/cuentas-por-pagar/movimientos-tc")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        movimientos.MapGet("/", async (
            [FromQuery] Guid? tarjetaId,
            [FromQuery] Guid? usuarioQueUsoId,
            [FromQuery] EstadoMovimientoTc? estado,
            [FromQuery] TipoMovimientoTc? tipo,
            [FromQuery] DateOnly? fechaDesde,
            [FromQuery] DateOnly? fechaHasta,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarMovimientosTcQuery(
                    tarjetaId, usuarioQueUsoId, estado, tipo,
                    fechaDesde, fechaHasta, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcLeer)
        .WithName("ListarMovimientosTc")
        .Produces<PagedResponse<MovimientoTcResponse>>(StatusCodes.Status200OK);

        movimientos.MapPost("/con-cfdi", async (
            [FromBody] RegistrarMovimientoTcConCfdiCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/movimientos-tc/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("RegistrarMovimientoTcConCfdi")
        .WithSummary("Flujo A — cargo con CFDI a nombre de Millet: genera FacturaProveedor y la marca Pagada")
        .Produces<MovimientoTcResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        movimientos.MapPost("/sin-cfdi", async (
            [FromBody] RegistrarMovimientoTcSinCfdiCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/movimientos-tc/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("RegistrarMovimientoTcSinCfdi")
        .WithSummary("Flujo B — cargo solo con ticket: solo movimiento (sin factura)")
        .Produces<MovimientoTcResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ============ F7-PR6: tipos especiales + disputas ============

        movimientos.MapPost("/refund", async (
            [FromBody] RegistrarRefundTcCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/movimientos-tc/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("RegistrarRefundTc")
        .WithSummary("Flujo E §5.5 — refund de un movimiento original (proveedor canceló compra)")
        .Produces<MovimientoRefundResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        movimientos.MapPost("/especial", async (
            [FromBody] RegistrarMovimientoEspecialTcCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/movimientos-tc/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("RegistrarMovimientoEspecialTc")
        .WithSummary("Intereses moratorios / Anualidad / Comisión por divisa (§8.2-§8.4)")
        .Produces<MovimientoEspecialResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        movimientos.MapPost("/{id:guid}/disputar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] DisputarBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new DisputarMovimientoTcCommand(
                    id, v, body.Motivo, body.FechaInicio),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcDisputar)
        .WithName("DisputarMovimientoTc")
        .WithSummary("§8.5 — marcar cargo desconocido o fraudulento, excluido del cierre hasta resolver")
        .Produces<MovimientoTcResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        movimientos.MapPost("/{id:guid}/resolver-disputa", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ResolverDisputaBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ResolverDisputaMovimientoTcCommand(
                    id, v, body.FueLegitimo),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcDisputar)
        .WithName("ResolverDisputaMovimientoTc")
        .Produces<MovimientoTcResponse>(StatusCodes.Status200OK);

        return app;
    }

    public sealed record ActualizarTarjetaBody(string NombreAlias, decimal LimiteCreditoMxn, short DiaCorte, short DiaLimitePago);
    public sealed record BloquearBody(string Motivo, DateOnly Fecha);
    public sealed record CancelarBody(DateOnly Fecha);
    public sealed record AgregarUsuarioBody(Guid EmpleadoId, DateOnly VigenciaDesde, DateOnly? VigenciaHasta, decimal? MontoMaxMensualMxn);
    public sealed record CerrarUsuarioBody(DateOnly Fecha);
    public sealed record DisputarBody(string Motivo, DateOnly FechaInicio);
    public sealed record ResolverDisputaBody(bool FueLegitimo);
}
