using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando la matriz de aprobación
/// queda satisfecha (todos los niveles requeridos registrados) y la
/// requisición transiciona a <see cref="EstadoRequisicion.Autorizada"/>.
///
/// <para>
/// Lo emite el agregado desde <see cref="Requisicion.RegistrarAutorizacion"/>
/// como parte del resultado <see cref="Requisicion.RegistrarAutorizacionResultado"/>;
/// el handler lo publica vía MediatR después de <c>SaveChanges</c>
/// y commit de la TX cross-port.
/// </para>
/// <para>
/// F4-PR2 emite el evento; los handlers reales (logging, telemetría,
/// notificaciones a stakeholders) se wirean en F4-PR4 según el
/// breakdown.
/// </para>
/// </summary>
public sealed record MatrizAprobacionSatisfechaEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    DateTimeOffset OcurridoEn) : INotification;
