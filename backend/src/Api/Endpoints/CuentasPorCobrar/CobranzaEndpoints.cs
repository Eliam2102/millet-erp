using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.CuentasPorCobrar.Application.Cobranza;
using Millet.CuentasPorCobrar.Application.Common;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.CuentasPorCobrar;

/// <summary>
/// Endpoints de seguimiento de cobranza (CXC-PR5, §11 del 01-diseño).
/// Append-only: no hay edición ni borrado — cada gestión deja su rastro.
/// Lectura con <c>cartera.leer</c> (ADR-0041); registro con
/// <c>cobranza.registrar</c>.
/// </summary>
public static class CobranzaEndpoints
{
    public static IEndpointRouteBuilder MapCobranzaEndpoints(this IEndpointRouteBuilder app)
    {
        var cobranza = app
            .MapGroup("/api/v1/cuentas-por-cobrar/cobranza")
            .WithTags("CuentasPorCobrar")
            .RequireAuthorization();

        cobranza.MapGet("/", async (
            [FromQuery] Guid clienteId,
            [FromQuery] ResultadoCobranza? resultado,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            IMediator mediator,
            CancellationToken cancellationToken) =>
        {
            var response = await mediator.Send(
                new ListarSeguimientosCobranzaQuery(clienteId, resultado, offset ?? 0, limit ?? 50),
                cancellationToken);
            return Results.Ok(response);
        })
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarSeguimientosCobranza")
        .Produces<PagedResponse<SeguimientoCobranzaResponse>>(StatusCodes.Status200OK);

        cobranza.MapPost("/", async (
            [FromBody] RegistrarSeguimientoCobranzaCommand command,
            IMediator mediator,
            DocumentoSucursalScope scope,
            CancellationToken cancellationToken) =>
        {
            await scope.VerificarAsync("cliente_cartera", command.ClienteId,
                PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales, cancellationToken);

            var response = await mediator.Send(command, cancellationToken);
            return Results.Created($"/api/v1/cuentas-por-cobrar/cobranza/{response.Id}", response);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("RegistrarSeguimientoCobranza")
        .Produces<SeguimientoCobranzaResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // M-241 · ficha de cobranza por factura vencida (CXC-GESTION). Alcance por sucursal de la factura (ADR-0051).
        cobranza.MapGet("/fichas", async (
            [FromQuery] Guid? clienteId, [FromQuery] Guid? responsableId, [FromQuery] bool? soloSinResponsable,
            IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new FichasCobranzaQuery(clienteId, responsableId, soloSinResponsable ?? false), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarFichasCobranza")
        .WithSummary("Facturas vencidas con su ficha: responsable, siguiente revisión y escalamiento (M-241)")
        .Produces<IReadOnlyList<FichaFacturaVencidaDto>>(StatusCodes.Status200OK);

        cobranza.MapGet("/fichas/{facturaCarteraId:guid}/eventos", async (
            Guid facturaCarteraId, IMediator mediator, CancellationToken cancellationToken) =>
            Results.Ok(await mediator.Send(new FichaCobranzaDetalleQuery(facturaCarteraId), cancellationToken)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCarteraLeer)
        .WithName("ListarEventosFichaCobranza")
        .WithSummary("Historial de la ficha de una factura, con quién y por qué (M-241)")
        .Produces<IReadOnlyList<FichaCobranzaEventoResponse>>(StatusCodes.Status200OK);

        cobranza.MapPost("/fichas/{facturaCarteraId:guid}/responsable", async (
            Guid facturaCarteraId, [FromBody] FichaResponsableBody body, DocumentoSucursalScope scope, IMediator mediator, CancellationToken ct) =>
        {
            await VerificarFacturaAsync(scope, facturaCarteraId, ct);
            return Results.Ok(await mediator.Send(new AsignarResponsableFichaCommand(
                facturaCarteraId, body.ResponsableId, body.ResponsableNombre, body.SiguienteRevision, body.Motivo), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("AsignarResponsableFichaCobranza")
        .WithSummary("Abre la ficha de la factura o reasigna al responsable, con rastro (M-241)")
        .Produces<FichaCobranzaResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cobranza.MapPost("/fichas/{facturaCarteraId:guid}/gestion", async (
            Guid facturaCarteraId, [FromBody] FichaGestionBody body, DocumentoSucursalScope scope, IMediator mediator, CancellationToken ct) =>
        {
            await VerificarFacturaAsync(scope, facturaCarteraId, ct);
            return Results.Ok(await mediator.Send(new RegistrarGestionFichaCommand(
                facturaCarteraId, body.Canal, body.Resultado, body.Nota, body.MontoComprometido, body.FechaComprometida, body.SiguienteRevision), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("RegistrarGestionFichaCobranza")
        .WithSummary("Registra una gestión; una promesa fija la siguiente revisión (M-241)")
        .Produces<FichaCobranzaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cobranza.MapPost("/fichas/{facturaCarteraId:guid}/reprogramar", async (
            Guid facturaCarteraId, [FromBody] FichaReprogramarBody body, DocumentoSucursalScope scope, IMediator mediator, CancellationToken ct) =>
        {
            await VerificarFacturaAsync(scope, facturaCarteraId, ct);
            return Results.Ok(await mediator.Send(new ReprogramarFichaCommand(facturaCarteraId, body.SiguienteRevision, body.Motivo), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("ReprogramarFichaCobranza")
        .WithSummary("Mueve la siguiente revisión con motivo (M-241)")
        .Produces<FichaCobranzaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cobranza.MapPost("/fichas/{facturaCarteraId:guid}/escalar", async (
            Guid facturaCarteraId, [FromBody] FichaEscalarBody body, DocumentoSucursalScope scope, IMediator mediator, CancellationToken ct) =>
        {
            await VerificarFacturaAsync(scope, facturaCarteraId, ct);
            return Results.Ok(await mediator.Send(new EscalarFichaCommand(facturaCarteraId, body.SupervisorId, body.SupervisorNombre, body.Motivo), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("EscalarFichaCobranza")
        .WithSummary("Escala la ficha a un supervisor: cambia el responsable y queda el rastro (M-241)")
        .Produces<FichaCobranzaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        cobranza.MapPost("/fichas/eventos/{eventoId:guid}/corregir", async (
            Guid eventoId, [FromBody] FichaCorregirBody body, CuentasPorCobrarDbContext scopeDb, DocumentoSucursalScope scope,
            IMediator mediator, CancellationToken ct) =>
        {
            var facturaId = await (from e in scopeDb.FichaCobranzaEventos.AsNoTracking()
                                   join f in scopeDb.FichasCobranza.AsNoTracking() on e.FichaCobranzaId equals f.Id
                                   where e.Id == eventoId select (Guid?)f.FacturaCarteraId).FirstOrDefaultAsync(ct);
            await VerificarFacturaAsync(scope, facturaId ?? Guid.Empty, ct);
            return Results.Ok(await mediator.Send(new CorregirEventoFichaCommand(eventoId, body.NotaCorregida, body.Motivo), ct));
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar)
        .WithName("CorregirEventoFichaCobranza")
        .WithSummary("Corrige la nota de una gestión sin editar el original; queda quién y por qué (M-241)")
        .Produces<FichaCobranzaResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    private static Task VerificarFacturaAsync(DocumentoSucursalScope scope, Guid facturaId, CancellationToken ct)
        => scope.VerificarAsync("factura_cartera", facturaId, PermisosCanonicos.CuentasPorCobrarCarteraGestionarTodasSucursales, ct);

    public sealed record FichaResponsableBody(Guid ResponsableId, string? ResponsableNombre, DateOnly? SiguienteRevision, string? Motivo);
    public sealed record FichaGestionBody(CanalCobranza Canal, ResultadoCobranza Resultado, string Nota,
        decimal? MontoComprometido, DateOnly? FechaComprometida, DateOnly? SiguienteRevision);
    public sealed record FichaReprogramarBody(DateOnly SiguienteRevision, string Motivo);
    public sealed record FichaEscalarBody(Guid SupervisorId, string? SupervisorNombre, string Motivo);
    public sealed record FichaCorregirBody(string NotaCorregida, string Motivo);
}
