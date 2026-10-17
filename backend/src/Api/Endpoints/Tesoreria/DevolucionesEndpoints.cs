using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Common;
using Millet.Tesoreria.Application.Devoluciones;
using Millet.Tesoreria.Domain.Devoluciones;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Devoluciones de dinero a clientes (T9.6): depósito erróneo y reembolso de
/// nota de crédito. La solicitud desde un depósito vive bajo
/// <c>movimientos/{id}/solicitar-devolucion</c> (hereda el alcance del movimiento).
/// </summary>
public static class DevolucionesEndpoints
{
    public sealed record EditarDevolucionBody(Guid ClienteId, decimal Importe, string Motivo, string? Banco, string? Clabe, string? Titular);
    public sealed record MotivoDevolucionBody(string Motivo);

    public static IEndpointRouteBuilder MapTesoreriaDevolucionesEndpoints(this IEndpointRouteBuilder app)
    {
        var devoluciones = app
            .MapGroup("/api/v1/tesoreria/devoluciones")
            .WithDocumentoSucursalScope("devolucion_cliente", "tesoreria.documentos", "id")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        devoluciones.MapGet("/", async (
            [FromQuery] EstadoDevolucionCliente? estado,
            [FromQuery] OrigenDevolucionCliente? origen,
            [FromQuery] Guid? clienteId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DevolucionesQuery(estado, origen, clienteId, offset ?? 0, limit ?? 50), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesVer)
        .WithName("ListarDevolucionesCliente")
        .Produces<PagedResponse<DevolucionClienteResponse>>(StatusCodes.Status200OK);

        devoluciones.MapGet("/conteos-por-estado", async (
            [FromQuery] OrigenDevolucionCliente? origen,
            [FromQuery] Guid? clienteId,
            IMediator mediator,
            CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ConteosDevolucionesPorEstadoQuery(origen, clienteId), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesVer)
        .WithName("ConteosDevolucionesClientePorEstado")
        .Produces<IReadOnlyList<Millet.SharedKernel.Application.ConteoPorEstadoItem>>(StatusCodes.Status200OK);

        devoluciones.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new DevolucionDetalleQuery(id), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesVer)
        .WithName("ObtenerDevolucionCliente")
        .Produces<DevolucionClienteResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // R2: transiciones con versión esperada (ADR-0012) e Idempotency-Key (ADR-0020).
        devoluciones.MapPut("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] EditarDevolucionBody body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new EditarDevolucionCommand(
                id, body.ClienteId, body.Importe, body.Motivo, body.Banco, body.Clabe, body.Titular, v), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesSolicitar)
        .WithName("EditarDevolucionCliente")
        .Produces<DevolucionClienteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        devoluciones.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] MotivoDevolucionBody body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new CancelarDevolucionCommand(id, body.Motivo, v), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesSolicitar)
        .WithName("CancelarDevolucionCliente")
        .Produces<DevolucionClienteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        devoluciones.MapPost("/{id:guid}/autorizar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new AutorizarDevolucionCommand(id, v), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesAutorizar)
        .WithName("AutorizarDevolucionCliente")
        .Produces<DevolucionClienteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        devoluciones.MapPost("/{id:guid}/rechazar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] MotivoDevolucionBody body,
            IMediator mediator,
            CancellationToken ct) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);
            return Results.Ok(await mediator.Send(new RechazarDevolucionCommand(id, body.Motivo, v), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesAutorizar)
        .WithName("RechazarDevolucionCliente")
        .Produces<DevolucionClienteResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapGet("/api/v1/tesoreria/clientes-lookup", async ([FromQuery] string? q, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new BuscarClientesTesoreriaQuery(q ?? ""), ct)))
        .RequireAuthorization()
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaDevolucionesSolicitar)
        .WithTags("Tesoreria");

        return app;
    }
}
