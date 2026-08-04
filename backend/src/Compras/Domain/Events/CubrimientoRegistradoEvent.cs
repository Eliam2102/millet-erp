using MediatR;

namespace Millet.Compras.Domain.Events;

/// <summary>
/// Evento de dominio in-proc emitido cuando una requisición registra el
/// cubrimiento de sus líneas tras la bifurcación stock-aware (F4-PR1).
/// El estado final ya transicionó a <c>Cerrada</c> (todo cubierto con
/// almacén) o <c>EnSurtido</c> (hay saldo a OC).
///
/// <para>
/// F4-PR1 emite el evento desde <see cref="Requisicion.RegistrarCubrimiento"/>;
/// los handlers reales (logging, telemetría, notificaciones) se wirean
/// en F4-PR4 según el breakdown.
/// </para>
/// <para>
/// Lleva un snapshot por línea (<see cref="CubrimientoLineaSnapshot"/>)
/// para que un consumer pueda auditar las cantidades sin tener que
/// re-hidratar la requisición.
/// </para>
/// </summary>
public sealed record CubrimientoRegistradoEvent(
    Guid RequisicionId,
    Guid EmpresaId,
    EstadoRequisicion EstadoFinal,
    DateTimeOffset OcurridoEn,
    IReadOnlyList<CubrimientoLineaSnapshot> Lineas) : INotification;

/// <summary>
/// Snapshot de cubrimiento por línea para el evento. Inmutable.
/// </summary>
public sealed record CubrimientoLineaSnapshot(
    Guid LineaId,
    decimal CantidadOriginal,
    decimal CantidadDeAlmacen,
    decimal CantidadDeCompra);
