using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido tras un rechazo (transición desde
/// <c>EnAutorizacionJefeCompras</c> o <c>EnAutorizacionDireccion</c> →
/// <c>Rechazada</c>). Lo consume el listener de notificaciones (F8)
/// para email al comprador con el motivo, y handlers de logging.
/// </summary>
public sealed record OrdenCompraRechazadaEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    NivelAutorizacion NivelRechazo,
    Guid MotivoRechazoId,
    string? MotivoRechazoTexto,
    Guid CompradorTitularId,
    Guid UsuarioRechazadorId,
    DateTimeOffset OcurridoEn) : INotification;
