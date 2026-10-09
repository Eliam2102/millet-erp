using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Integration;

namespace Millet.CuentasPorPagar.Application.Integration.Mappers;

/// <summary>
/// Listener del <see cref="FacturaProveedorAutorizadaDomainEvent"/> que
/// publica el <see cref="PasivoAutorizadoParaPagoIntegrationEvent"/>
/// con los datos enriquecidos de la factura (montos, vencimiento,
/// UUID/folio del proveedor) para Tesorería.
///
/// <para>
/// Coexiste con <c>FacturaProveedorAutorizadaMapper</c> (informativo a
/// Compras/Contabilidad) — ambos suscriben el mismo domain event y
/// publican payloads distintos.
/// </para>
/// </summary>
public sealed class PasivoAutorizadoParaPagoMapper
    : INotificationHandler<FacturaProveedorAutorizadaDomainEvent>
{
    private readonly IIntegrationEventPublisher _publisher;
    private readonly CuentasPorPagarDbContext _db;
    private readonly FacturaProveedor.Elegibilidad.ElegibilidadFacturaService _elegibilidad;

    public PasivoAutorizadoParaPagoMapper(
        IIntegrationEventPublisher publisher,
        CuentasPorPagarDbContext db, FacturaProveedor.Elegibilidad.ElegibilidadFacturaService elegibilidad)
    {
        _publisher = publisher;
        _db = db;
        _elegibilidad = elegibilidad;
    }

    public Task Handle(FacturaProveedorAutorizadaDomainEvent notification, CancellationToken cancellationToken) =>
        PublicarAsync(notification, cancellationToken);

    public async Task PublicarAsync(FacturaProveedorAutorizadaDomainEvent notification, CancellationToken cancellationToken,
        bool considerarRecepcionesLocales = false)
    {
        // Resolver datos de la factura para enriquecer el payload — la
        // factura ya está en el ChangeTracker (lo acaba de modificar el
        // handler de Autorizar), así que la query es local.
        var f = _db.FacturasProveedor.Local.FirstOrDefault(x => x.Id == notification.FacturaProveedorId)
            ?? await _db.FacturasProveedor.Include(x => x.Lineas).FirstOrDefaultAsync(x => x.Id == notification.FacturaProveedorId, cancellationToken);
        if (f is null) return;
        var elegibilidad = await _elegibilidad.CalcularAsync(f, cancellationToken, considerarRecepcionesLocales);

        await _publisher.PublishAsync(new PasivoAutorizadoParaPagoIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.FechaAutorizacion,
            FacturaProveedorId: notification.FacturaProveedorId,
            ProveedorId: f.ProveedorId,
            OrdenCompraId: f.OrdenCompraId,
            MontoTotal: f.Total,
            SaldoPendiente: elegibilidad.ElegiblePendiente,
            Moneda: f.Moneda,
            TipoCambio: f.TipoCambio,
            FechaVencimiento: f.FechaVencimiento,
            UuidCfdi: f.UuidCfdi,
            FolioProveedor: f.FolioProveedor,
            MetodoPago: f.MetodoPago,
            TipoBeneficiario: PasivoAutorizadoParaPagoIntegrationEvent.BeneficiarioProveedor,
            BeneficiarioId: f.ProveedorId,
            OrigenTipo: PasivoAutorizadoParaPagoIntegrationEvent.OrigenFactura,
            OrigenId: notification.FacturaProveedorId), cancellationToken);
    }
}
