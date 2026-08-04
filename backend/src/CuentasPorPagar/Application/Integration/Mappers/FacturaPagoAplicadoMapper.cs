using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

/// <summary>
/// Listener del <see cref="FacturaProveedorPagoAplicadoDomainEvent"/> que
/// publica <see cref="FacturaPagoAplicadoIntegrationEvent"/> con el
/// acumulado pagado por OC ya calculado, para que Compras derive el
/// sub-estado Pago sin proyección espejo (cierra el PLATFORM-TODO
/// <c>&lt;TesoreriaEventListenerCompras&gt;</c>).
///
/// <para>
/// El acumulado = Σ <c>importe_pagado</c> de las DEMÁS facturas de la
/// misma OC (query) + el <c>ImportePagadoFactura</c> del evento (el de
/// la factura corriente viene del agregado en memoria, porque el
/// SaveChanges que persiste el cambio ocurre después de este mapper en
/// la misma transacción del listener).
/// </para>
/// </summary>
public sealed class FacturaPagoAplicadoMapper
    : INotificationHandler<FacturaProveedorPagoAplicadoDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly CuentasPorPagarDbContext _db;

    public FacturaPagoAplicadoMapper(
        IIntegrationEventPublisher publisher,
        CuentasPorPagarDbContext db)
    {
        _publisher = publisher;
        _db = db;
    }

    public async Task Handle(
        FacturaProveedorPagoAplicadoDomainEvent notification, CancellationToken cancellationToken)
    {
        // Sin OC no hay sub-estado que actualizar en Compras (factura
        // capturada sin conciliación — gasto directo, TC, etc.).
        if (notification.OrdenCompraId is not Guid ordenCompraId) return;

        var pagadoOtrasFacturas = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(f => f.OrdenCompraId == ordenCompraId
                && f.Id != notification.FacturaProveedorId)
            .SumAsync(f => f.ImportePagado, cancellationToken);

        await _publisher.PublishAsync(new FacturaPagoAplicadoIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.OcurridoEn,
            FacturaProveedorId: notification.FacturaProveedorId,
            OrdenCompraId: ordenCompraId,
            ImportePagadoFactura: notification.ImportePagadoFactura,
            MontoPagadoAcumuladoOc: pagadoOtrasFacturas + notification.ImportePagadoFactura),
            cancellationToken);
    }
}
