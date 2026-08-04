using MediatR;

namespace Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.Events;

/// <summary>
/// Domain event publicado cuando una <see cref="NotaCreditoProveedor"/>
/// con <c>TipoRelacionCfdi=Devolucion (3)</c> es capturada y se vincula
/// a una <c>NotaCargo</c> con <c>DevolucionAProveedorId</c> no nulo
/// (F6-PR3). El mapper lo traduce a
/// <c>NotaCreditoFiscalDevolucionRecibidaIntegrationEvent</c> que
/// Almacén consume para marcar la devolución como conciliada con NC
/// fiscal.
/// </summary>
public sealed record NotaCreditoFiscalDevolucionRecibidaDomainEvent(
    Guid EmpresaId,
    Guid NotaCreditoProveedorId,
    Guid NotaCargoId,
    Guid DevolucionAProveedorId,
    Guid ProveedorId,
    decimal Total,
    string UuidCfdi,
    DateTimeOffset OcurridoEn) : INotification;
