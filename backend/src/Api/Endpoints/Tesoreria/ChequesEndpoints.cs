using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Identidad.Domain;
using Millet.Tesoreria.Application.Cheques;
using Millet.Tesoreria.Domain.Cheques;

namespace Millet.Api.Endpoints.Tesoreria;

/// <summary>M-372: cheques recibidos (registrar, confirmar contra estado de cuenta o a mano, rebote).</summary>
public static class ChequesEndpoints
{
    public sealed record ConfirmarChequeBody(Guid? ExtractoLineaId, DateOnly? FechaValor, string? Referencia);
    public sealed record RebotarChequeBody(DateOnly Fecha, string Motivo);

    public static IEndpointRouteBuilder MapTesoreriaChequesEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/tesoreria/cheques").WithTags("Tesoreria").RequireAuthorization();
        const string ver = PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosVer;
        const string registrar = PermissionPolicyProvider.Prefix + PermisosCanonicos.TesoreriaMovimientosRegistrar;

        g.MapGet("/", async ([FromQuery] EstadoChequeRecibido? estado, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ChequesRecibidosQuery(estado), ct)))
            .RequireAuthorization(ver).WithName("ListarChequesRecibidos")
            .Produces<IReadOnlyList<ChequeRecibidoResponse>>(StatusCodes.Status200OK);

        g.MapGet("/{id:guid}/lineas-candidatas", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new LineasCandidatasChequeQuery(id), ct)))
            .RequireAuthorization(ver).WithName("LineasCandidatasCheque")
            .Produces<IReadOnlyList<LineaCandidataChequeResponse>>(StatusCodes.Status200OK);

        g.MapPost("/", async ([FromBody] RegistrarChequeRecibidoCommand body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(body, ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(registrar).WithName("RegistrarChequeRecibido")
            .Produces<ChequeRecibidoResponse>(StatusCodes.Status200OK).ProducesValidationProblem();

        g.MapPost("/{id:guid}/confirmar", async (Guid id, [FromBody] ConfirmarChequeBody body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ConfirmarChequeRecibidoCommand(id, body.ExtractoLineaId, body.FechaValor, body.Referencia), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(registrar).WithName("ConfirmarChequeRecibido")
            .Produces<ChequeRecibidoResponse>(StatusCodes.Status200OK).ProducesValidationProblem();

        g.MapPost("/{id:guid}/rebotar", async (Guid id, [FromBody] RebotarChequeBody body, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new RebotarChequeRecibidoCommand(id, body.Fecha, body.Motivo), ct)))
            .WithMetadata(new RequireIdempotencyKeyAttribute())
            .RequireAuthorization(registrar).WithName("RebotarChequeRecibido")
            .Produces<ChequeRecibidoResponse>(StatusCodes.Status200OK).ProducesValidationProblem();

        return app;
    }
}
