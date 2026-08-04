using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.CerrarManual;

/// <summary>
/// Handler de <see cref="CerrarManualOcCommand"/>. Carga la OC con sus
/// líneas, invoca <see cref="Millet.Compras.Domain.Oc.OrdenCompra.CerrarManual"/>
/// (que valida Estado == Autorizada), persiste y publica el
/// <see cref="Millet.Compras.Domain.Oc.Events.OrdenCompraCerradaEvent"/>
/// devuelto — mismo evento y mismo patrón post-SaveChanges que el cierre
/// automático de los listeners de la triada.
/// </summary>
public sealed class CerrarManualOcHandler : IRequestHandler<CerrarManualOcCommand>
{
    private readonly ComprasDbContext _db;
    private readonly IClock _clock;
    private readonly IPublisher _publisher;

    public CerrarManualOcHandler(
        ComprasDbContext db,
        IClock clock,
        IPublisher publisher)
    {
        _db = db;
        _clock = clock;
        _publisher = publisher;
    }

    public async Task Handle(CerrarManualOcCommand command, CancellationToken cancellationToken)
    {
        var oc = await _db.OrdenesCompra
            .Include(o => o.Lineas)
            .FirstOrDefaultAsync(o => o.Id == command.OrdenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException(
                "ORDEN_COMPRA_NO_ENCONTRADA",
                $"No se encontró orden de compra con id '{command.OrdenCompraId}' en la empresa actual.");

        var cerrada = oc.CerrarManual(_clock.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        await _publisher.Publish(cerrada, cancellationToken);
    }
}
