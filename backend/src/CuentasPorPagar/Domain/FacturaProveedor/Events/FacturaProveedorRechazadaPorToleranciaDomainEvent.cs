using MediatR;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;

/// <summary>
/// Evento in-process emitido cuando la captura de una factura con OC
/// excede la tolerancia y la factura queda en
/// <see cref="EstadoPasivo.Cancelada"/> con motivo
/// <see cref="MotivoCancelacion.RechazadaPorTolerancia"/>. Compras
/// suscribe el integration event para regresar la OC a revisión /
/// corrección.
/// </summary>
public sealed record FacturaProveedorRechazadaPorToleranciaDomainEvent(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid OrdenCompraId,
    decimal TotalFactura,
    decimal TotalOc,
    decimal Diferencia,
    string ToleranciaAplicada,
    DateTimeOffset OcurridoEn) : INotification;
