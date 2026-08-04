using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarNotaCredito;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AutorizarFactura;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CancelarFactura;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Application.FacturaProveedor.EditarCabecera;
using Millet.CuentasPorPagar.Application.FacturaProveedor.EnviarARevision;
using Millet.CuentasPorPagar.Application.FacturaProveedor.LiberarRevision;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del agregado <c>FacturaProveedor</c> (F3-PR1). Cubre
/// la captura con OC + listado paginado + detalle + edición
/// pre-autorización + cancelación. Eventos de outbox (F3-PR2) y revisión
/// (F4) entran después.
///
/// <para>
/// Convenciones del proyecto aplicadas: Idempotency-Key en POSTs que
/// crean recursos (ADR-0020), ETag/If-Match transmitido al cliente como
/// header <c>X-Expected-Version</c> (simplificación de F3-PR1; en F4 se
/// migra al header HTTP estándar <c>If-Match</c>). Problem Details vía
/// <c>IExceptionHandler</c> global.
/// </para>
/// </summary>
public static class FacturasEndpoints
{
    public static IEndpointRouteBuilder MapFacturasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/facturas")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] EstadoPasivo? estado,
            [FromQuery] Guid? proveedorId,
            [FromQuery] Guid? sucursalId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarFacturasQuery(estado, proveedorId, sucursalId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLeer)
        .WithName("ListarFacturasProveedor")
        .WithSummary("Bandeja paginada de facturas de proveedor")
        .Produces<PagedResponse<FacturaListItemResponse>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", async (
            Guid id,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(new GetFacturaPorIdQuery(id), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLeer)
        .WithName("ObtenerFacturaProveedor")
        .Produces<FacturaDetalleResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            [FromBody] CapturarFacturaConOcCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/facturas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasCapturar)
        .WithName("CapturarFacturaConOc")
        .WithSummary("Captura una factura conciliada con una OC")
        .WithDescription(
            "Resuelve la OC vía IComprasOcReadPort, calcula diferencia y tolerancia del " +
            "proveedor (snapshot), persiste la factura. Si la diferencia excede la " +
            "tolerancia, la factura se cancela inmediatamente con motivo " +
            "RechazadaPorTolerancia (el evento al módulo Compras lo emite F3-PR2). " +
            "Permiso `cuentas_por_pagar.facturas.capturar` + `Idempotency-Key` obligatorio.")
        .Produces<CapturarFacturaConOcResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] EditarCabeceraRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    detail: "PATCH requiere el header X-Expected-Version con el Version actual de la factura (concurrencia optimista, ADR-0012).",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new EditarCabeceraFacturaCommand(
                    Id: id,
                    VersionEsperada: v,
                    FolioProveedor: request.FolioProveedor,
                    SerieProveedor: request.SerieProveedor,
                    FechaVencimiento: request.FechaVencimiento,
                    FechaContabilizacion: request.FechaContabilizacion),
                cancellationToken);

            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasEditar)
        .WithName("EditarCabeceraFactura")
        .WithSummary("Edita la cabecera de una factura en estado Capturada")
        .Produces<EditarCabeceraFacturaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapGet("/en-revision", async (
            [FromQuery] Guid dependenciaRevisoraId,
            [FromQuery] Guid? motivoRevisionId,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new FacturasEnRevisionPorAreaQuery(dependenciaRevisoraId, motivoRevisionId, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLeer)
        .WithName("ListarFacturasEnRevisionPorArea")
        .WithSummary("Bandeja de facturas en revisión asignadas a una dependencia")
        .Produces<PagedResponse<FacturaEnRevisionResponse>>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/enviar-revision", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] EnviarRevisionRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new EnviarFacturaARevisionCommand(id, v, request.MotivoRevisionId, request.DependenciaRevisoraId),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasEnviarRevision)
        .WithName("EnviarFacturaARevision")
        .Produces<EnviarFacturaARevisionResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/liberar-revision", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] LiberarRevisionRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new LiberarRevisionFacturaCommand(id, v, request.AccionTomada),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasLiberarRevision)
        .WithName("LiberarRevisionFactura")
        .Produces<LiberarRevisionFacturaResponse>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/autorizar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    detail: "Autorizar requiere el header X-Expected-Version (ADR-0012).",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new AutorizarFacturaCommand(id, v),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasAutorizar)
        .WithName("AutorizarFacturaManual")
        .WithSummary("Autoriza manualmente una factura (override §A7 / pre-F4)")
        .Produces<AutorizarFacturaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        group.MapPost("/{id:guid}/cancelar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] CancelarFacturaRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    detail: "Cancelar requiere el header X-Expected-Version (ADR-0012).",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new CancelarFacturaCommand(id, v, request.Motivo, request.Texto),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarFacturasCancelar)
        .WithName("CancelarFactura")
        .WithSummary("Cancela una factura en estado Capturada o EnRevision")
        .Produces<CancelarFacturaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // F6-PR2: aplicar NC a saldo de factura
        group.MapPost("/{id:guid}/aplicar-nc", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] AplicarNcRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    detail: "Aplicar NC requiere el header X-Expected-Version con la Version actual de la factura (ADR-0012).",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new AplicarNotaCreditoAFacturaCommand(
                    FacturaId: id,
                    FacturaVersionEsperada: v,
                    NotaCreditoId: request.NotaCreditoId,
                    NotaCreditoVersionEsperada: request.NotaCreditoVersionEsperada,
                    Monto: request.Monto),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar)
        .WithName("AplicarNotaCreditoAFactura")
        .WithSummary("Aplica una NC al saldo de una factura del mismo proveedor")
        .WithDescription(
            "Decrementa simultáneamente el `SaldoPorAplicar` de la NC y aumenta " +
            "`NcAplicadasTotal` de la factura dentro de un mismo SaveChanges. " +
            "Requiere que la NC esté vinculada a esta factura como `FacturaOrigenId`. " +
            "Headers obligatorios: `X-Expected-Version` (de la factura), `Idempotency-Key`.")
        .Produces<AplicarNotaCreditoAFacturaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        // F6-PR2: aplicar anticipo a saldo de factura
        group.MapPost("/{id:guid}/aplicar-anticipo", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] AplicarAnticipoRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
            {
                return Results.Problem(
                    title: "X-Expected-Version requerido",
                    detail: "Aplicar anticipo requiere el header X-Expected-Version con la Version actual de la factura (ADR-0012).",
                    statusCode: StatusCodes.Status428PreconditionRequired);
            }

            var response = await mediator.Send(
                new AplicarAnticipoAFacturaCommand(
                    FacturaId: id,
                    FacturaVersionEsperada: v,
                    AnticipoId: request.AnticipoId,
                    AnticipoVersionEsperada: request.AnticipoVersionEsperada,
                    Monto: request.Monto),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarAnticiposCapturar)
        .WithName("AplicarAnticipoAFactura")
        .WithSummary("Amortiza un anticipo contra el saldo de una factura del mismo proveedor")
        .WithDescription(
            "Decrementa simultáneamente `SaldoAmortizable` del anticipo y aumenta " +
            "`AnticipoAplicadoTotal` de la factura dentro de un mismo SaveChanges. " +
            "Headers obligatorios: `X-Expected-Version` (de la factura), `Idempotency-Key`.")
        .Produces<AplicarAnticipoAFacturaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
        .ProducesProblem(StatusCodes.Status428PreconditionRequired);

        return app;
    }

    public sealed record EditarCabeceraRequest(
        string? FolioProveedor,
        string? SerieProveedor,
        DateOnly FechaVencimiento,
        DateTimeOffset FechaContabilizacion);

    public sealed record CancelarFacturaRequest(MotivoCancelacion Motivo, string? Texto);

    public sealed record EnviarRevisionRequest(Guid MotivoRevisionId, Guid DependenciaRevisoraId);

    public sealed record LiberarRevisionRequest(string AccionTomada);

    public sealed record AplicarNcRequest(Guid NotaCreditoId, int NotaCreditoVersionEsperada, decimal Monto);

    public sealed record AplicarAnticipoRequest(Guid AnticipoId, int AnticipoVersionEsperada, decimal Monto);
}
