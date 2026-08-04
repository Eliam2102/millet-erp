using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.UnidadesMedida;
using Serilog.Context;

namespace Millet.Compras.Application.RegistrarRecepcion;

/// <summary>
/// Handler de <see cref="RegistrarRecepcionCommand"/> (F5-PR1):
/// <list type="number">
///   <item>Carga la requisición con sus líneas.</item>
///   <item>Invoca <c>requisicion.RegistrarRecepcion(...)</c>: el agregado
///         valida estado (<c>EnSurtido</c>), aplica la recepción a la
///         línea y, si todas las líneas tienen <c>CantidadPendiente == 0</c>,
///         transiciona a <c>Cerrada</c> y devuelve el evento de cierre.</item>
///   <item>Persiste y publica <see cref="Domain.Events.RequisicionCerradaEvent"/>
///         si vino, vía <c>IMediator</c> post-SaveChanges.</item>
/// </list>
///
/// <para>
/// Sin <c>ICurrentUserContext</c>: el comando lo invoca un handler
/// in-proc reaccionando a un evento del submódulo OC, no un endpoint
/// HTTP. La autoría queda en logs vía el evento mismo.
/// </para>
/// </summary>
public sealed class RegistrarRecepcionHandler : IRequestHandler<RegistrarRecepcionCommand, Unit>
{
    private readonly ComprasDbContext _db;
    private readonly IMediator _mediator;
    private readonly IDecimalesUnidadGuard _decimalesGuard;

    public RegistrarRecepcionHandler(
        ComprasDbContext db,
        IMediator mediator,
        IDecimalesUnidadGuard decimalesGuard)
    {
        _db = db;
        _mediator = mediator;
        _decimalesGuard = decimalesGuard;
    }

    public async Task<Unit> Handle(RegistrarRecepcionCommand command, CancellationToken cancellationToken)
    {
        // F8-PR2: trazas en App Insights para investigar latencia / cierres
        // post-recepción. Activado en el listener in-proc de OC.
        using var _ = LogContext.PushProperty("RequisicionId", command.RequisicionId);
        using var activity = ComprasActivitySource.Instance.StartActivity("Compras.RegistrarRecepcion");
        activity?.SetTag("compras.requisicion.id", command.RequisicionId);
        activity?.SetTag("compras.linea.id", command.LineaRequisicionId);

        var requisicion = await _db.Requisiciones
            .Include(r => r.Lineas)
            .FirstOrDefaultAsync(r => r.Id == command.RequisicionId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "REQUISICION_NO_ENCONTRADA",
                $"No se encontró requisición con id '{command.RequisicionId}' en la empresa actual.");

        activity?.SetTag("compras.estado.antes", requisicion.Estado.ToString());

        // ADR-0046 Etapa 2: valida los decimales de la cantidad recibida contra
        // la unidad del artículo de la línea (defensivo; la cantidad nace en la
        // recepción de almacén — 2b valida en el origen). FK NULL → no valida.
        var lineaRecepcion = requisicion.Lineas
            .FirstOrDefault(l => l.Id == command.LineaRequisicionId);
        if (lineaRecepcion is not null)
        {
            await _decimalesGuard.ValidarAsync(
                new[]
                {
                    new CantidadAValidar(
                        lineaRecepcion.ArticuloId,
                        command.CantidadRecibida,
                        lineaRecepcion.UnidadMedida),
                },
                cancellationToken);
        }

        var cerradaEvento = requisicion.RegistrarRecepcion(
            lineaId: command.LineaRequisicionId,
            cantidadRecibida: command.CantidadRecibida,
            ocurridoEn: command.OcurridoEn);

        // F6-PR3: publicar antes del SaveChanges para que el integration
        // mapper de Cerrada inserte la fila a outbox en la misma TX.
        if (cerradaEvento is not null)
        {
            await _mediator.Publish(cerradaEvento, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);

        activity?.SetTag("compras.estado.despues", requisicion.Estado.ToString());
        return Unit.Value;
    }
}
