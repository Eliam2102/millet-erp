using MediatR;

namespace Millet.CuentasPorPagar.Domain.AnticipoProveedor.Events;

/// <summary>
/// Evento in-process emitido al capturar un anticipo. Compras suscribe
/// el integration event para vincular el anticipo a la solicitud
/// original (§8.1).
/// </summary>
public sealed record AnticipoProveedorCapturadoDomainEvent(
    Guid EmpresaId,
    Guid AnticipoId,
    Guid ProveedorId,
    decimal MontoEntregado,
    Guid? OrdenCompraId,
    DateTimeOffset OcurridoEn) : INotification;
