using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Listener in-proc (F5-PR2) que consume eventos de recepción de
/// material emitidos por Almacén y actualiza la línea de OC vía
/// <c>OrdenCompra.RegistrarRecepcionLinea</c>. Tras recalcular
/// sub-estados, publica <see cref="OrdenCompraCerradaEvent"/> si la OC
/// se cerró automáticamente.
///
/// <para>
/// <b>Idempotencia</b>: el payload trae el acumulado, no el delta.
/// Repetir el mismo evento es no-op a nivel agregado.
/// </para>
/// </summary>
public sealed class RecepcionMaterialEnOcListener
    : INotificationHandler<RecepcionMaterialEnOcEvent>
{
    private readonly ComprasDbContext _db;
    private readonly IPublisher _publisher;
    private readonly ILogger<RecepcionMaterialEnOcListener> _logger;

    public RecepcionMaterialEnOcListener(
        ComprasDbContext db,
        IPublisher publisher,
        ILogger<RecepcionMaterialEnOcListener> logger)
    {
        _db = db;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task Handle(RecepcionMaterialEnOcEvent notification, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == notification.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró OC '{notification.OrdenCompraId}' al registrar recepción.");

        var resultado = oc.RegistrarRecepcionLinea(
            notification.LineaOrdenCompraId,
            notification.CantidadAcumulada,
            notification.OcurridoEn);

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recepción aplicada. OcId={OcId} LineaId={LineaId} CantidadAcumulada={Cantidad} Cerrada={Cerrada}",
            notification.OrdenCompraId,
            notification.LineaOrdenCompraId,
            notification.CantidadAcumulada,
            resultado.Cerrada is not null);

        if (resultado.Cerrada is { } cerrada)
        {
            await _publisher.Publish(cerrada, cancellationToken);
        }
    }
}
