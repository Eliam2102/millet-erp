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

    public PasivoAutorizadoParaPagoMapper(
        IIntegrationEventPublisher publisher,
        CuentasPorPagarDbContext db)
    {
        _publisher = publisher;
        _db = db;
    }

    public async Task Handle(
        FacturaProveedorAutorizadaDomainEvent notification, CancellationToken cancellationToken)
    {
        // Resolver datos de la factura para enriquecer el payload — la
        // factura ya está en el ChangeTracker (lo acaba de modificar el
        // handler de Autorizar), así que la query es local.
        var f = await _db.FacturasProveedor
            .AsNoTracking()
            .Where(x => x.Id == notification.FacturaProveedorId)
            .Select(x => new
            {
                x.ProveedorId,
                x.OrdenCompraId,
                x.Total,
                Saldo = x.Total - x.AnticipoAplicadoTotal - x.NcAplicadasTotal - x.ImportePagado,
                x.Moneda,
                x.TipoCambio,
                x.FechaVencimiento,
                x.UuidCfdi,
                x.FolioProveedor,
                x.MetodoPago,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (f is null) return; // factura borrada concurrentemente — no publicar

        await _publisher.PublishAsync(new PasivoAutorizadoParaPagoIntegrationEvent(
            EmpresaId: notification.EmpresaId,
            OcurridoEn: notification.FechaAutorizacion,
            FacturaProveedorId: notification.FacturaProveedorId,
            ProveedorId: f.ProveedorId,
            OrdenCompraId: f.OrdenCompraId,
            MontoTotal: f.Total,
            SaldoPendiente: f.Saldo,
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
