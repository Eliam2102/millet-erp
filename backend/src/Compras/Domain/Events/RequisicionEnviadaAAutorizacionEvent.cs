using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando una requisición transiciona
/// de <c>Borrador</c> a <c>EnAutorizacion</c> (vía
/// <see cref="Requisicion.EnviarAAutorizacion"/>). Implementa
/// <see cref="INotification"/> de MediatR para que handlers in-proc
/// (logging, telemetría, notificaciones) lo reciban.
///
/// F2-PR3 emite el evento; los handlers reales (logging + notificaciones)
/// se wirean en F4-PR4 según el breakdown.
/// </summary>
public sealed record RequisicionEnviadaAAutorizacionEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    string Folio,
    DateTimeOffset OcurridoEn) : INotification;
