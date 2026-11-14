using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Ports;
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

        // M-326 (b): redondeo de hasta $0.99 al pagar. Solicita quien registra pagos; autoriza/rechaza el Gerente de Área (otro usuario).
        app.MapPost("/api/v1/tesoreria/pagos/redondeo", async (
            [FromBody] SolicitarRedondeoPagoCommand command, IMediator mediator, CancellationToken ct) =>
            Results.Created($"/api/v1/tesoreria/pagos/redondeo/{command.FacturaProveedorId}", await mediator.Send(command, ct)))
            .WithTags("Tesoreria").WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosAplicar)
            .WithName("SolicitarRedondeoPago")
            .Produces<RedondeoPagoResponse>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/tesoreria/pagos/redondeo/{id:guid}/autorizar", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AutorizarRedondeoPagoCommand(id), ct)))
            .WithTags("Tesoreria").WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosAutorizarRedondeo)
            .WithName("AutorizarRedondeoPago")
            .Produces<RedondeoPagoResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/api/v1/tesoreria/pagos/redondeo/{id:guid}/rechazar", async (
            Guid id, [FromBody] RechazarRedondeoPagoBody body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RechazarRedondeoPagoCommand(id, body.Motivo), ct)))
            .WithTags("Tesoreria").WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaPagosAutorizarRedondeo)
            .WithName("RechazarRedondeoPago")
            .Produces<RedondeoPagoResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);

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

        // TES-09 (86e3jvvj4): TC del catálogo K10.6 para prellenar «TC del pago» en pagos y cobros en moneda extranjera.
        app.MapGet("/api/v1/tesoreria/tipo-cambio-pago", async (
            [FromQuery] string moneda,
            [FromQuery] DateOnly fecha,
            ITipoCambioPagoReadPort tiposCambio,
            CancellationToken cancellationToken) =>
        {
            var codigo = (moneda ?? "").Trim().ToUpperInvariant();
            if (codigo.Length != 3)
                return Results.Problem(title: "Moneda inválida", statusCode: StatusCodes.Status400BadRequest);
            var valor = codigo == "MXN" ? null : await tiposCambio.ValorEnMxnAsync(codigo, fecha, cancellationToken);
            return Results.Ok(new TipoCambioPagoResponse(codigo, fecha, valor));
        })
        .WithTags("Tesoreria")
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer)
        .WithName("ObtenerTipoCambioPagoTesoreria")
        .Produces<TipoCambioPagoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest);

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

    /// <summary>TES-09: TC del catálogo para la fecha del pago; <c>ValorEnMxn</c> null si el catálogo no lo tiene o la moneda es MXN.</summary>
    public sealed record TipoCambioPagoResponse(string Moneda, DateOnly Fecha, decimal? ValorEnMxn);
}

public sealed record RechazarRedondeoPagoBody(string Motivo);
