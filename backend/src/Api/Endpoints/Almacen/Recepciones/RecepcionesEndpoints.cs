using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Almacen.Recepciones;

/// <summary>
/// Endpoints de Recepciones del módulo Almacén (F2-PR2). Bajo
/// <c>/api/v1/almacen/recepciones</c>. Auth con permisos canónicos
/// <c>almacen.entradas.*</c>. POST de captura+registro requiere
/// Idempotency-Key (ADR-0020).
/// </summary>
public static class RecepcionesEndpoints
{
    private const int LimitMax = 500;

    public static IEndpointRouteBuilder MapRecepcionesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/almacen/recepciones")
            .WithTags("Almacen")
            .RequireAuthorization();

        // --- Listar Recepciones (bandeja) ---
        group.MapGet("/", async (
            [FromQuery] EstadoMovimiento? estado,
            [FromQuery] Guid? subAlmacenId,
            [FromQuery] Guid? ordenCompraId,
            [FromQuery] DateOnly? desde,
            [FromQuery] DateOnly? hasta,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            [FromQuery] Guid? proveedorId,
            [FromQuery] string? q,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var (off, lim) = NormalizePaging(offset, limit);
            var response = await mediator.Send(
                new ListarRecepcionesQuery(estado, subAlmacenId, ordenCompraId, desde, hasta, off, lim, proveedorId, q), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasLeer)
        .WithName("ListarRecepciones")
        .WithSummary("Listar recepciones (bandeja con filtros estado/sub-almacén/OC/fecha)")
        .Produces<AlmacenPagedResponse<RecepcionListItem>>(StatusCodes.Status200OK);

        // --- Obtener Recepción por Id ---
        group.MapGet("/{id:guid}", async (
            Guid id,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("recepcion", id, PermisosCanonicos.AlmacenEntradasLeerTodasSucursales, ct);
            var detalle = await mediator.Send(new ObtenerRecepcionPorIdQuery(id), ct);
            return detalle is null ? Results.NotFound() : Results.Ok(detalle);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasLeer)
        .WithName("ObtenerRecepcionPorId")
        .WithSummary("Obtener recepción con líneas")
        .Produces<RecepcionDetalle>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- CFDI por procesar del proveedor de la OC (selector de la recepción) ---
        group.MapGet("/ordenes/{ordenCompraId:guid}/cfdis", async (
            Guid ordenCompraId,
            DocumentoSucursalScope documentos,
            Millet.Almacen.Domain.Ports.IComprasOcReadPort ocPort,
            Millet.Almacen.Domain.Ports.IProveedorReadPort proveedores,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("orden_compra", ordenCompraId, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            var oc = await ocPort.ObtenerAsync(ordenCompraId, ct)
                ?? throw new EntityNotFoundException("RECEPCION_OC_NO_ENCONTRADA", "La orden de compra no existe; selecciona una orden autorizada.");
            var rfc = (await proveedores.ObtenerAsync(oc.ProveedorId, ct))?.Rfc;
            var response = await mediator.Send(new ListarCfdisParaRecepcionQuery(rfc), ct);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithName("ListarCfdisParaRecepcion")
        .WithSummary("CFDI por procesar del proveedor de la OC para vincular a la recepción")
        .Produces<CfdisParaRecepcionResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Líneas recibibles de la OC (pendiente, tolerancia y máximo por línea; P-20) ---
        group.MapGet("/ordenes/{ordenCompraId:guid}/lineas-recibibles", async (
            Guid ordenCompraId,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("orden_compra", ordenCompraId, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            return Results.Ok(await mediator.Send(new LineasRecibiblesQuery(ordenCompraId), ct));
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithName("ListarLineasRecibiblesOc")
        .WithSummary("Pendiente, tolerancia y máximo recibible por línea de OC (mismo cálculo que el registro)")
        .Produces<IReadOnlyList<LineaRecibibleDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound);

        // --- Registrar Recepción Variante A (factura/CFDI) ---
        group.MapPost("/", async (
            RegistrarRecepcionConFacturaCommand command,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("orden_compra", command.OrdenCompraId, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            foreach (var bin in (command.Lineas ?? []).Select(l => l.UbicacionId).OfType<Guid>().Distinct())
                await documentos.VerificarAsync("ubicacion_almacen", bin, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            // D10: un CFDI por procesar no tiene sucursal; lo valida el handler (RFC del proveedor de la OC).
            if (command.CfdiRecibidoId is Guid cfdi
                && (await documentos.Puerto("cfdi_recibido").ListarAsync("cfdi_recibido", ct)).FirstOrDefault(x => x.Id == cfdi) is { Sucursales.Count: > 0 })
                await documentos.VerificarAsync("cfdi_recibido", cfdi, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/recepciones/{response.RecepcionId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarRecepcionConFactura")
        .WithSummary("Registrar recepción Variante A (con factura/CFDI conocido)")
        .Produces<RegistrarRecepcionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // --- Registrar Recepción Variante B (packing list, factura pendiente) ---
        group.MapPost("/packing-list", async (
            RegistrarRecepcionConPackingListCommand command,
            DocumentoSucursalScope documentos,
            IMediator mediator,
            CancellationToken ct) =>
        {
            await documentos.VerificarAsync("orden_compra", command.OrdenCompraId, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            foreach (var bin in (command.Lineas ?? []).Select(l => l.UbicacionId).OfType<Guid>().Distinct())
                await documentos.VerificarAsync("ubicacion_almacen", bin, PermisosCanonicos.AlmacenEntradasGestionarTodasSucursales, ct);
            var response = await mediator.Send(command, ct);
            return Results.Created(
                $"/api/v1/almacen/recepciones/{response.RecepcionId}",
                response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.AlmacenEntradasRegistrar)
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("RegistrarRecepcionConPackingList")
        .WithSummary("Registrar recepción Variante B (materiales directos no-vidrio, factura pendiente)")
        .Produces<RegistrarRecepcionResponse>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static (int off, int lim) NormalizePaging(int? offset, int? limit)
    {
        var off = offset is < 0 ? 0 : offset ?? 0;
        var lim = limit is null or <= 0 ? 50 : Math.Min(limit.Value, LimitMax);
        return (off, lim);
    }
}
