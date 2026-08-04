using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Cuentas;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>
/// Endpoints HTTP del catálogo de cuentas bancarias propias (TES-PR2 +
/// TES-7 revisada): GET con saldos + CRUD en Tesorería con el permiso
/// <c>tesoreria.cuentas.administrar</c>. El seed script
/// (backend/scripts/seed-cuentas-bancarias-tesoreria.sql) queda solo para
/// bootstrap de ambientes. Número de cuenta y CLABE salen enmascarados
/// salvo permiso <c>tesoreria.movimientos.ver-cuenta-completa</c>
/// (resuelto en query/handlers); por eso la CLABE es write-only en la
/// actualización (null = sin cambio, <c>limpiarClabe</c> = borrar) y el
/// número de cuenta es inmutable post-creación.
/// </summary>
public static class CuentasEndpoints
{
    public static IEndpointRouteBuilder MapTesoreriaCuentasEndpoints(this IEndpointRouteBuilder app)
    {
        var cuentas = app
            .MapGroup("/api/v1/tesoreria/cuentas")
            .WithTags("Tesoreria")
            .RequireAuthorization();

        cuentas.MapGet("/", async (
            [FromQuery] bool? soloActivas,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new SaldosPorCuentaQuery(soloActivas ?? true), cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasVer)
        .WithName("ListarCuentasBancariasConSaldo")
        .Produces<IReadOnlyList<CuentaSaldoResponse>>(StatusCodes.Status200OK);

        cuentas.MapPost("/", async (
            [FromBody] CrearCuentaBancariaCommand command,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/tesoreria/cuentas/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasAdministrar)
        .WithName("CrearCuentaBancaria")
        .Produces<CuentaSaldoResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cuentas.MapPut("/{id:guid}", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            [FromBody] ActualizarCuentaBody body,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            if (expectedVersion is not int v)
                return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

            var response = await mediator.Send(
                new ActualizarCuentaBancariaCommand(
                    id, body.Banco, body.Moneda, body.Clabe, body.LimpiarClabe,
                    body.CuentaContableRef, body.PerfilExtracto, v),
                cancellationToken);
            return Results.Ok(response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasAdministrar)
        .WithName("ActualizarCuentaBancaria")
        .Produces<CuentaSaldoResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cuentas.MapPost("/{id:guid}/activar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
                await CambiarEstado(id, expectedVersion, activa: true, mediator, cancellationToken))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasAdministrar)
        .WithName("ActivarCuentaBancaria")
        .Produces<CuentaSaldoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        cuentas.MapPost("/{id:guid}/desactivar", async (
            Guid id,
            [FromHeader(Name = "X-Expected-Version")] int? expectedVersion,
            IMediator mediator,
            CancellationToken cancellationToken) =>
                await CambiarEstado(id, expectedVersion, activa: false, mediator, cancellationToken))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaCuentasAdministrar)
        .WithName("DesactivarCuentaBancaria")
        .Produces<CuentaSaldoResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CambiarEstado(
        Guid id, int? expectedVersion, bool activa, IMediator mediator, CancellationToken cancellationToken)
    {
        if (expectedVersion is not int v)
            return Results.Problem(title: "X-Expected-Version requerido", statusCode: StatusCodes.Status428PreconditionRequired);

        var response = await mediator.Send(
            new CambiarEstadoCuentaBancariaCommand(id, activa, v), cancellationToken);
        return Results.Ok(response);
    }

    public sealed record ActualizarCuentaBody(
        string Banco,
        string Moneda,
        string? Clabe,
        bool LimpiarClabe,
        string? CuentaContableRef,
        string? PerfilExtracto);
}
