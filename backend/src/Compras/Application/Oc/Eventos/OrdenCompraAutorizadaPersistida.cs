using MediatR;

namespace Millet.Compras.Application.Oc.Eventos;

/// <summary>
/// Notificación local posterior al guardado de autorización + outbox.
/// Permite generar el PDF sin depender del orden de los mappers de integración.
/// </summary>
public sealed record OrdenCompraAutorizadaPersistida(Guid OrdenCompraId, string Folio) : INotification;
