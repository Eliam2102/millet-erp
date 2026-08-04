using MediatR;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;

/// <summary>
/// Evento in-process emitido cuando una factura transiciona a
/// <see cref="EstadoPasivo.Autorizada"/> (§A7). Informativo para
/// Compras y Contabilidad; el evento <c>PasivoAutorizadoParaPagoEvent</c>
/// (a Tesorería) se publica desde el flujo de Tesorería en F9-PR1.
/// </summary>
public sealed record FacturaProveedorAutorizadaDomainEvent(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid? OrdenCompraId,
    DateTimeOffset FechaAutorizacion) : INotification;
