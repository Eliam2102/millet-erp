using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido tras autorización N2 exitosa (transición
/// <c>EnAutorizacionDireccion → Autorizada</c>). Lo consume el listener
/// del servicio PDF (F6-PR1) para generar el PDF institucional, el
/// listener de notificaciones (F8) para email al comprador, y el
/// módulo Recepción cuando exista (visibilidad).
/// </summary>
public sealed record OrdenCompraAutorizadaEvent(
    Guid OrdenCompraId,
    Guid EmpresaId,
    string Folio,
    Guid CompradorTitularId,
    DateTimeOffset FechaContabilizacion,
    DateTimeOffset OcurridoEn) : INotification;
