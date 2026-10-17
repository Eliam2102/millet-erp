using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Bancos;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP del pago a proveedor (TES-PR4, §11 del 01-diseño): el
/// operador ejecuta la transferencia en la banca y aquí registra el hecho
/// bancario contra pasivos de la bandeja. Publica los eventos espejo
/// congelados hacia CxP vía outbox.
/// </summary>
public static class PagosEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaPagosEndpoints(this IEndpointRouteBuilder app)
    {
        var pagos = app
            .MapGroup("/api/v1/tesoreria/pagos")
            .WithDocumentoSucursalScope("pago_proveedor", "tesoreria.documentos", "pagoId")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        pagos.MapPost("/", async (
            [FromBody] RegistrarPagoProveedorCommand command,
            IMediator mediator,
            DocumentoSucursalScope scope,
            CancellationToken cancellationToken) =>
        {
            foreach (var aplicacion in command.Aplicaciones ?? [])
                await scope.VerificarAsync("factura_proveedor", aplicacion.FacturaProveedorId,
                    PermisosCanonicos.TesoreriaDocumentosGestionarTodasSucursales, cancellationToken);

            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/movimientos/{response.MovimientoId}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosAplicar)
        .WithName("RegistrarPagoProveedor")
        .Produces<PagoProveedorResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // 86e3pdmee: cuentas del proveedor para elegir a cuál pagar (número enmascarado salvo permiso PII).
        app.MapGet("/api/v1/tesoreria/proveedores/{proveedorId:guid}/cuentas", async (
            Guid proveedorId,
            IProveedorBancoReadPort proveedores,
            ICurrentUserPermissions permisos,
            CancellationToken cancellationToken) =>
        {
            var proveedor = await proveedores.ObtenerAsync(proveedorId, cancellationToken);
            if (proveedor is null) return Results.NotFound();
            var completa = await permisos.TieneAsync(PermisosCanonicos.TesoreriaMovimientosVerCuentaCompleta, cancellationToken);
            return Results.Ok(proveedor.Cuentas.Select(c => c with { Numero = completa ? c.Numero : Clabe.Enmascarar(c.Numero) }).ToList());
        })
        .WithTags("Tesoreria")
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosAplicar)
        .WithName("ListarCuentasProveedorParaPago")
        .Produces<IReadOnlyList<CuentaProveedorDto>>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        pagos.MapPost("/{pagoId:guid}/revertir", async (
            Guid pagoId,
            [FromBody] RevertirPagoBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new RevertirPagoProveedorCommand(pagoId, body.Motivo), cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosRevertir)
        .WithName("RevertirPagoProveedor")
        .Produces<PagoProveedorResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    public sealed record RevertirPagoBody(string Motivo);
}
