using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Compras.Application.Lineas.ActualizarLinea;
using Millet.Compras.Application.Lineas.ActualizarNotasLinea;
using Millet.Compras.Application.Lineas.AgregarLinea;
using Millet.Compras.Application.Lineas.EliminarLinea;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Compras;

/// <summary>
/// Endpoints HTTP de líneas de requisición (F2-PR2). Anidados bajo
/// <c>/api/v1/compras/requisiciones/{id}/lineas</c>:
///
/// <list type="bullet">
///   <item><c>POST   /lineas</c>                     — agregar línea (Borrador).</item>
///   <item><c>PATCH  /lineas/{lineaId}</c>           — replace estructural (Borrador, sin cubrimiento).</item>
///   <item><c>PATCH  /lineas/{lineaId}/notas</c>     — actualizar solo notas (cualquier estado no-terminal).</item>
///   <item><c>DELETE /lineas/{lineaId}</c>           — eliminar línea (Borrador).</item>
/// </list>
///
/// Todos requieren permiso <c>compras.requisiciones.editar</c>. Las
/// invariantes de estado y cubrimiento las maneja el agregado raíz
/// <c>Millet.Compras.Domain.Requisicion</c> (→ 422 vía
/// <c>BusinessRuleException</c>).
/// </summary>
public static class LineasEndpoints
{
    public static IEndpointRouteBuilder MapLineasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/compras/requisiciones/{id:guid}/lineas")
            .WithTags("Compras")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.ComprasRequisicionesEditar);

        group.MapPost("/", async (
            Guid id,
            [FromBody] AgregarLineaRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var command = new AgregarLineaCommand(
                RequisicionId: id,
                ArticuloId: request.ArticuloId,
                Cantidad: request.Cantidad,
                UnidadMedida: request.UnidadMedida,
                PrecioEstimadoMonto: request.PrecioEstimadoMonto,
                PrecioEstimadoMoneda: request.PrecioEstimadoMoneda,
                CuentaContableId: request.CuentaContableId,
                CentroCostoId: request.CentroCostoId,
                Proyecto: request.Proyecto,
                FechaRequerida: request.FechaRequerida,
                Notas: request.Notas);

            var response = await mediator.Send(command, cancellationToken);
            return Results.Created(
                $"/api/v1/compras/requisiciones/{id}/lineas/{response.Id}",
                response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("AgregarLinea")
        .WithSummary("Agregar línea a Borrador")
        .WithDescription(
            "Solo aplicable en estado Borrador. Validación cross-table " +
            "verifica que `ArticuloId` exista en `compartido.articulos` y " +
            "esté `Activo` (404 / 422 si no). Header `Idempotency-Key` " +
            "obligatorio.")
        .Produces<AgregarLineaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/{lineaId:guid}", async (
            Guid id,
            Guid lineaId,
            [FromBody] ActualizarLineaRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var command = new ActualizarLineaCommand(
                RequisicionId: id,
                LineaId: lineaId,
                ArticuloId: request.ArticuloId,
                Cantidad: request.Cantidad,
                UnidadMedida: request.UnidadMedida,
                PrecioEstimadoMonto: request.PrecioEstimadoMonto,
                PrecioEstimadoMoneda: request.PrecioEstimadoMoneda,
                CuentaContableId: request.CuentaContableId,
                CentroCostoId: request.CentroCostoId,
                Proyecto: request.Proyecto,
                FechaRequerida: request.FechaRequerida);

            await mediator.Send(command, cancellationToken);
            return Results.NoContent();
        })
        .WithName("ActualizarLinea")
        .WithSummary("Reemplazo estructural de línea (Borrador)")
        .WithDescription(
            "PATCH equivalente a un replace completo de la línea (excepto " +
            "notas, que tienen endpoint propio). Solo en estado Borrador — " +
            "una vez transmitida la RQ, las cantidades quedan congeladas. " +
            "Mismo shape de validación que AgregarLinea.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPatch("/{lineaId:guid}/notas", async (
            Guid id,
            Guid lineaId,
            [FromBody] ActualizarNotasLineaRequest request,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(
                new ActualizarNotasLineaCommand(id, lineaId, request.Notas),
                cancellationToken);
            return Results.NoContent();
        })
        .WithName("ActualizarNotasLinea")
        .WithSummary("Actualizar solo `notas` de una línea (cualquier estado no terminal)")
        .WithDescription(
            "Permite editar notas en RQs ya transmitidas o autorizadas (caso " +
            "de uso: el comprador agrega una observación al proveedor). NO " +
            "afecta cantidad ni precio — esos están congelados post-Borrador.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapDelete("/{lineaId:guid}", async (
            Guid id,
            Guid lineaId,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            await mediator.Send(new EliminarLineaCommand(id, lineaId), cancellationToken);
            return Results.NoContent();
        })
        .WithName("EliminarLinea")
        .WithSummary("Eliminar línea de Borrador")
        .WithDescription(
            "Solo en Borrador. La RQ debe quedar con al menos una línea para " +
            "transmitirse — si eliminás la última línea, ese chequeo lo hará " +
            "el handler de `Transmitir` (TRANSMITIR_SIN_LINEAS = 422).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Body del POST. Mismo shape que <see cref="AgregarLineaCommand"/> sin <c>RequisicionId</c> (viene del path).</summary>
    public sealed record AgregarLineaRequest(
        Guid ArticuloId,
        decimal Cantidad,
        string UnidadMedida,
        decimal PrecioEstimadoMonto,
        string PrecioEstimadoMoneda,
        Guid? CuentaContableId,
        Guid? CentroCostoId,
        string? Proyecto,
        DateOnly? FechaRequerida,
        string? Notas);

    /// <summary>Body del PATCH estructural. <c>RequisicionId</c> y <c>LineaId</c> vienen del path.</summary>
    public sealed record ActualizarLineaRequest(
        Guid ArticuloId,
        decimal Cantidad,
        string UnidadMedida,
        decimal PrecioEstimadoMonto,
        string PrecioEstimadoMoneda,
        Guid? CuentaContableId,
        Guid? CentroCostoId,
        string? Proyecto,
        DateOnly? FechaRequerida);

    /// <summary>Body del PATCH de notas.</summary>
    public sealed record ActualizarNotasLineaRequest(string? Notas);
}
