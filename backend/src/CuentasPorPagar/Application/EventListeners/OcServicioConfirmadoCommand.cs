using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.CuentasPorPagar.Domain.Eventos;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.EventListeners;

/// <summary>
/// Listener de <c>compras.orden-compra.servicio-confirmado.v1</c> (86e3tacbn). Compras confirmó (o deshizo) el servicio de
/// una OC: CxP republica el pasivo de sus facturas Autorizadas con la elegibilidad nueva (las líneas de servicio solo
/// cuentan como recibidas con la OC confirmada). La elegibilidad se lee de Compras, ya con el cambio guardado.
/// Idempotente por <see cref="EventoProcesado"/>.
/// </summary>
public sealed record OcServicioConfirmadoCommand(Guid EventId, OcServicioConfirmadoPayload Payload) : IRequest;

public sealed class OcServicioConfirmadoHandler(CuentasPorPagarDbContext db, IClock clock,
    Integration.Mappers.PasivoAutorizadoParaPagoMapper pasivos, ILogger<OcServicioConfirmadoHandler> logger)
    : IRequestHandler<OcServicioConfirmadoCommand>
{
    public const string EventType = "compras.orden-compra.servicio-confirmado.v1";

    public async Task Handle(OcServicioConfirmadoCommand request, CancellationToken cancellationToken)
    {
        if (await db.EventosProcesados.AsNoTracking()
                .AnyAsync(e => e.EventoId == request.EventId && e.EventoTipo == EventType, cancellationToken))
            return;

        var p = request.Payload;
        var ahora = clock.UtcNow;
        db.EventosProcesados.Add(new EventoProcesado(request.EventId, EventType, ahora,
            $"OC={p.OrdenCompraId} Folio={p.Folio} Confirmado={p.Confirmado}"));

        var autorizadas = await db.FacturasProveedor.Where(f => f.OrdenCompraId == p.OrdenCompraId &&
            f.Estado == Domain.FacturaProveedor.EstadoPasivo.Autorizada).ToListAsync(cancellationToken);
        foreach (var f in autorizadas)
            await pasivos.PublicarAsync(new Domain.FacturaProveedor.Events.FacturaProveedorAutorizadaDomainEvent(
                f.EmpresaId, f.Id, f.OrdenCompraId, ahora), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("[OcServicioConfirmadoHandler] OC {OcId} servicio confirmado={Confirmado}: {Pasivos} pasivo(s) republicado(s).",
            p.OrdenCompraId, p.Confirmado, autorizadas.Count);
    }
}
