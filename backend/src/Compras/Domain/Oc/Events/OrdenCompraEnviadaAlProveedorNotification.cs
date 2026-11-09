using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Notificación in-process emitida tras el envío exitoso de una orden de compra al proveedor
/// (resultado <c>aceptado</c>, <c>simulado</c> o <c>registrado</c>) en entornos no productivos.
/// La consume CuentasPorPagar para disparar la generación automática del CFDI 4.0 sintético de prueba.
/// </summary>
public sealed record OrdenCompraEnviadaAlProveedorNotification(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio) : INotification;
