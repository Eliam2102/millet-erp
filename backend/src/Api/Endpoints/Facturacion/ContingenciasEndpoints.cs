using MediatR;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Reportes;
using Millet.Facturacion.Application.Timbrado.Contingencias;
using Millet.Identidad.Domain;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Bitácora de contingencias de timbrado (F6.8): consulta, registro manual,
/// regularización manual y reporte mensual. <c>/api/v1/facturacion/contingencias-timbrado</c>.
/// </summary>
public static class ContingenciasEndpoints
{
    public sealed record RegularizarContingenciaRequest(string Nota);

    public static IEndpointRouteBuilder MapFacturacionContingenciasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/facturacion/contingencias-timbrado").WithTags("Facturacion");

        group.MapGet("/", async (int? anio, int? mes, string? estado, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ContingenciasQuery(anio, mes, estado), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionFacturasLeer)
        .WithName("ContingenciasTimbrado")
        .WithSummary("Bitácora de contingencias de timbrado")
        .Produces<ContingenciasResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        group.MapGet("/reporte", async (int anio, int mes, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReporteContingenciasQuery(anio, mes), ct)))
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionReportesLeer)
        .WithName("ReporteContingenciasTimbrado")
        .WithSummary("Reporte mensual de contingencias de timbrado")
        .Produces<ReporteResponse<ReporteContingenciaFila>>(StatusCodes.Status200OK)
        .ProducesValidationProblem();

        group.MapPost("/", async (RegistrarContingenciaCommand body, IMediator mediator, CancellationToken ct) =>
        {
            var item = await mediator.Send(body, ct);
            return Results.Created($"/api/v1/facturacion/contingencias-timbrado/{item.Id}", item);
        })
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionComprobantesReintentarTimbrado)
        .WithName("RegistrarContingenciaTimbrado")
        .WithSummary("Registra a mano una contingencia (SAT, internet u otra causa)")
        .Produces<ContingenciaItem>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/regularizar", async (
            Guid id, RegularizarContingenciaRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new RegularizarContingenciaCommand(id, body.Nota), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionComprobantesReintentarTimbrado)
        .WithName("RegularizarContingenciaTimbrado")
        .WithSummary("Regulariza a mano una contingencia sin documento afectado")
        .Produces<ContingenciaItem>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
