using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Common;
using Millet.CuentasPorPagar.Application.TarjetaCredito.EstadosCuenta;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorPagar;

/// <summary>
/// Endpoints HTTP del agregado <c>EstadoCuentaTc</c> (F7-PR5). Cubre
/// el ciclo: crear → subir archivo → conciliar automático. La
/// confirmación manual de matches y captura retroactiva entran en
/// F7-PR6.
/// </summary>
public static class EstadosCuentaTcEndpoints
{
    public static IEndpointRouteBuilder MapEstadosCuentaTcEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/cuentas-por-pagar/estados-cuenta-tc")
            .WithTags("CuentasPorPagar")
            .RequireAuthorization();

        group.MapGet("/", async (
            [FromQuery] Guid? tarjetaId,
            [FromQuery] EstadoCuentaTcStatus? estado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarEstadosCuentaTcQuery(tarjetaId, estado, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcLeer)
        .WithName("ListarEstadosCuentaTc")
        .Produces<PagedResponse<EstadoCuentaTcResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (
            [FromBody] CrearEstadoCuentaTcCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-pagar/estados-cuenta-tc/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("CrearEstadoCuentaTc")
        .WithSummary("Auxiliar crea un estado de cuenta para una tarjeta + periodo (antes de subir el archivo del banco)")
        .Produces<EstadoCuentaTcResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Upload del archivo del banco: multipart/form-data porque puede
        // pesar hasta 50 MB y los browsers serializan bien archivos así.
        group.MapPost("/{id:guid}/archivo", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            HttpRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            if (!request.HasFormContentType)
            {
                return Results.Problem(
                    title: "multipart/form-data requerido",
                    detail: "Use Content-Type multipart/form-data y campo 'archivo'.",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);
            }

            var form = await request.ReadFormAsync(cancellationToken);
            var archivo = form.Files.GetFile("archivo");
            if (archivo is null || archivo.Length == 0)
            {
                return Results.Problem(
                    title: "Archivo requerido",
                    detail: "Adjunte el archivo del banco en el campo 'archivo' del form.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            using var stream = new MemoryStream();
            await archivo.CopyToAsync(stream, cancellationToken);
            var contenido = stream.ToArray();

            var response = await mediator.Send(
                new SubirArchivoEstadoCuentaTcCommand(
                    Id: id,
                    VersionEsperada: v,
                    NombreArchivo: archivo.FileName,
                    ContentType: archivo.ContentType,
                    Contenido: contenido),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("SubirArchivoEstadoCuentaTc")
        .WithSummary("Sube el Excel del banco, calcula SHA-256, parsea según perfil y persiste líneas")
        .DisableAntiforgery()
        .Produces<SubirArchivoEstadoCuentaTcResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/conciliar-automatico", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ConciliarAutomaticoEstadoCuentaTcCommand(id, v), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("ConciliarAutomaticoEstadoCuentaTc")
        .WithSummary("Corre el algoritmo de match §7 — auto-match ≥90, sugerencia 60-89, sin match <60")
        .Produces<ConciliarAutomaticoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // ============ F7-PR6: cierre del ciclo ============

        group.MapPost("/{id:guid}/lineas/{lineaId:guid}/confirmar-match", async (
            Guid id, Guid lineaId,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ConfirmarMatchBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ConfirmarMatchLineaBancoCommand(
                    id, v, lineaId, body.MovimientoTcId, body.DiferenciaCambiariaMxn),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("ConfirmarMatchLineaBanco")
        .WithSummary("Auxiliar confirma una sugerencia 60-89 — promueve el match y registra dif. cambiaria si aplica")
        .Produces<EstadoCuentaTcResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/lineas/{lineaId:guid}/capturar-retroactiva", async (
            Guid id, Guid lineaId,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] CapturarRetroactivaBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new CapturarMovimientoDesdeLineaCommand(
                    id, v, lineaId, body.UsuarioQueUsoId, body.ConceptoContable),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcRegistrarMovimiento)
        .WithName("CapturarMovimientoDesdeLineaBanco")
        .WithSummary("D13 — captura retroactiva del cargo desde una línea sin match")
        .Produces<EstadoCuentaTcResponse>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/marcar-conciliado", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new MarcarEstadoCuentaConciliadoCommand(id, v),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcCerrarEstadoCuenta)
        .WithName("MarcarEstadoCuentaConciliado")
        .WithSummary("EnConciliacion → Conciliado: diferencia 0, todas las líneas atadas/explicadas")
        .Produces<EstadoCuentaTcResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/cerrar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new CerrarEstadoCuentaTcCommand(id, v),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcCerrarEstadoCuenta)
        .WithName("CerrarEstadoCuentaTc")
        .WithSummary("Cierra el EC, genera FacturaProveedor agregada contra el banco y publica EstadoCuentaTcCerradoEvent")
        .Produces<CerrarEstadoCuentaTcResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/marcar-pagado-banco", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new MarcarEstadoCuentaTcPagadoBancoCommand(id, v),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorPagarTcCerrarEstadoCuenta)
        .WithName("MarcarEstadoCuentaPagadoBanco")
        .WithSummary("Proxy MVP de Tesorería — marca pagado al banco + batch update de movimientos")
        .Produces<EstadoCuentaTcResponse>(StatusCodes.Status200OK);

        return app;
    }

    public sealed record ConfirmarMatchBody(Guid MovimientoTcId, decimal? DiferenciaCambiariaMxn);
    public sealed record CapturarRetroactivaBody(Guid UsuarioQueUsoId, string ConceptoContable);
}
