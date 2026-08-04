using MediatR;

namespace Millet.CuentasPorPagar.Domain.NotaCargo.Events;

/// <summary>
/// Evento in-process emitido cuando una NotaCargo pasa a
/// <see cref="EstadoNotaCargo.Autorizada"/> por Dirección (§8.1).
/// Compras suscribe informativamente.
/// </summary>
public sealed record NotaCargoAutorizadaDomainEvent(
    Guid EmpresaId,
    Guid NotaCargoId,
    Guid ProveedorId,
    decimal Monto,
    Guid? FacturaOrigenId,
    DateTimeOffset OcurridoEn) : INotification;
