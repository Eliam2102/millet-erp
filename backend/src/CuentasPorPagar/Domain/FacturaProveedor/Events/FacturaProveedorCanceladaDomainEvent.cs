using MediatR;

namespace Millet.CuentasPorPagar.Domain.FacturaProveedor.Events;

/// <summary>
/// Evento in-process emitido cuando una factura transiciona a
/// <see cref="EstadoPasivo.Cancelada"/> **excepto** por
/// <see cref="MotivoCancelacion.RechazadaPorTolerancia"/> (ese caso usa
/// <see cref="FacturaProveedorRechazadaPorToleranciaDomainEvent"/>).
/// Compras suscribe para decrementar <c>CantidadFacturada</c>; el resto
/// de cancelaciones no afectan OC.
/// </summary>
public sealed record FacturaProveedorCanceladaDomainEvent(
    Guid EmpresaId,
    Guid FacturaProveedorId,
    Guid? OrdenCompraId,
    MotivoCancelacion Motivo,
    string? MotivoTexto,
    DateTimeOffset OcurridoEn) : INotification;
