using MediatR;
using Microsoft.AspNetCore.Mvc;
using Millet.Api.Auth;
using Millet.Api.Web;
using Millet.Facturacion.Application.Cajas.Terminal;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Api.Endpoints.Facturacion;

/// <summary>
/// Terminal de tarjetas (86e3vr1tf, Parte A): carga del archivo diario del banco,
/// emparejamiento con los cobros de caja y liquidación por folio. Parte B (86e3xt2q8):
/// factura mensual del banco ligada a los cargos. Supervisión de
/// caja (<c>facturacion.caja.supervisar</c>).
/// </summary>
public static class TerminalTarjetaEndpoints
{
    private const long TamanoMaximo = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapFacturacionTerminalTarjetaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/v1/facturacion/terminal-tarjetas")
            .WithTags("Facturacion")
            .RequireAuthorization(PermissionPolicyProvider.Prefix + PermisosCanonicos.FacturacionCajaSupervisar);

        group.MapPost("/cargas", async (
            IFormFile? archivo, [FromForm] DateOnly? fechaArchivo, IMediator mediator, CancellationToken ct) =>
        {
            if (archivo is null || archivo.Length == 0)
                throw new BusinessRuleException("ARCHIVO_REQUERIDO", "Envía el archivo de la terminal en el campo 'archivo'.");
            if (archivo.Length > TamanoMaximo)
                throw new BusinessRuleException("TERMINAL_ARCHIVO_GRANDE", "El archivo de la terminal no debe pasar de 5 MB.");
            if (fechaArchivo is not DateOnly fecha)
                throw new BusinessRuleException("TERMINAL_FECHA_REQUERIDA", "Indica el día de las transacciones del archivo.");
            using var memoria = new MemoryStream();
            await archivo.CopyToAsync(memoria, ct);
            return Results.Ok(await mediator.Send(new CargarArchivoTerminalCommand(archivo.FileName, memoria.ToArray(), fecha), ct));
        })
        .DisableAntiforgery()
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("CargarArchivoTerminal")
        .WithSummary("Carga el archivo diario de la terminal de tarjetas y empareja con los cobros de caja")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<CargaTerminalResponse>()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/cargas", async ([FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CargasTerminalQuery(limit ?? 20), ct)))
        .WithName("ListarCargasTerminal")
        .Produces<IReadOnlyList<CargaTerminalResponse>>();

        group.MapGet("/transacciones", async (
            [FromQuery] EstadoTransaccionTerminal? estado, [FromQuery] int? offset, [FromQuery] int? limit,
            IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new TransaccionesTerminalQuery(estado, offset ?? 0, limit ?? 50), ct)))
        .WithName("ListarTransaccionesTerminal")
        .Produces<TransaccionesTerminalPagina>();

        group.MapPost("/reemparejar", async (IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new ReemparejarTerminalCommand(), ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("ReemparejarTerminal")
        .WithSummary("Vuelve a emparejar las ventas sin emparejar con los cobros de caja")
        .Produces<CargaTerminalResponse>();

        // ---- Parte B (86e3xt2q8): factura mensual del banco ligada a los cargos ----
        group.MapGet("/factura-banco/candidatos", async (
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] string? rfcEmisor, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CfdisBancoCandidatosQuery(anio, mes, rfcEmisor), ct)))
        .WithName("ListarCfdisBancoTerminal")
        .WithSummary("CFDI recibidos (buzón de CxP) candidatos a factura del banco del mes")
        .Produces<IReadOnlyList<Millet.Facturacion.Domain.Ports.CfdiRecibidoBanco>>();

        group.MapGet("/factura-banco/cuadre", async (
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] Guid? cfdiRecibidoId, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new CuadreFacturaBancoQuery(anio, mes, cfdiRecibidoId), ct)))
        .WithName("CuadreFacturaBancoTerminal")
        .WithSummary("Compara la comisión y el IVA de los cargos del mes contra la factura del banco")
        .Produces<CuadreFacturaBancoResponse>();

        group.MapGet("/factura-banco", async ([FromQuery] int? limit, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new FacturasBancoTerminalQuery(limit ?? 24), ct)))
        .WithName("ListarFacturasBancoTerminal")
        .Produces<IReadOnlyList<FacturaBancoTerminalResponse>>();

        group.MapPost("/factura-banco", async (LigarFacturaBancoTerminalCommand body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(body, ct)))
        .WithMetadata(new RequireIdempotencyKeyAttribute())
        .WithName("LigarFacturaBancoTerminal")
        .WithSummary("Liga la factura mensual del banco a los cargos del mes si cuadra al centavo")
        .Produces<FacturaBancoTerminalResponse>()
        .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
