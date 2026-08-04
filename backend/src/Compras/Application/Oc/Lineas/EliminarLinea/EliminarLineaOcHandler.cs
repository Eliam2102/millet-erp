using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Domain.Oc.Events;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Lineas.EliminarLinea;

/// <summary>
/// Elimina una línea de OC. Si la línea era la última que apuntaba a una
/// RQ (F4-PR3), libera la RQ en la misma TX vía
/// <see cref="Domain.Requisicion.LiberarDeOc"/> y publica
/// <see cref="LineaRqLiberadaEvent"/> para auditoría/listeners.
/// </summary>
public sealed class EliminarLineaOcHandler : IRequestHandler<EliminarLineaOcCommand>
{
    private readonly ComprasDbContext _db;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public EliminarLineaOcHandler(
        ComprasDbContext db,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task Handle(EliminarLineaOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        var resultado = oc.EliminarLinea(command.LineaId);

        if (resultado.RequisicionIdALiberar is Guid rqId)
        {
            var rq = await _db.Requisiciones
                .FirstOrDefaultAsync(r => r.Id == rqId, cancellationToken)
                ?? throw new EntityNotFoundException(
                    "REQUISICION_NO_ENCONTRADA",
                    $"No se encontró requisición '{rqId}' a liberar.");
            rq.LiberarDeOc();
        }

        await _db.SaveChangesAsync(cancellationToken);

        if (resultado.RequisicionIdALiberar is Guid liberada)
        {
            await _publisher.Publish(
                new LineaRqLiberadaEvent(
                    RequisicionId: liberada,
                    OrdenCompraId: oc.Id,
                    EmpresaId: oc.EmpresaId,
                    CantidadLiberada: null,
                    OcurridoEn: _clock.UtcNow),
                cancellationToken);
        }
    }
}
