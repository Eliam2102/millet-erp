using MediatR;

namespace Millet.Compras.Application.RegistrarRecepcion;

/// <summary>
/// Comando para registrar una recepción incremental de material desde
/// OC sobre una línea de requisición (F5-PR1). Lo invoca el handler
/// in-proc de <see cref="Domain.Ports.OrdenCompra.OcRecepcionRegistradaEvent"/>
/// (puente entre OC submódulo y Compras) — no hay endpoint HTTP, el
/// flujo es 100% event-driven.
///
/// <para>
/// <see cref="OcurridoEn"/> viene del evento original de OC (la
/// fecha-hora real de la recepción), no del clock del handler. Si el
/// caller no la provee, usar <c>DateTimeOffset.UtcNow</c>.
/// </para>
/// </summary>
public sealed record RegistrarRecepcionCommand(
    Guid RequisicionId,
    Guid LineaRequisicionId,
    decimal CantidadRecibida,
    DateTimeOffset OcurridoEn) : IRequest<Unit>;
