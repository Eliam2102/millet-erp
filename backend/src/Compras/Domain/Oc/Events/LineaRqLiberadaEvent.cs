using MediatR;

namespace Millet.Compras.Domain.Oc.Events;

/// <summary>
/// Evento in-process emitido cuando una RQ deja de estar comprometida
/// en una OC. Causas (F4-PR3):
/// <list type="bullet">
///   <item>Eliminar la última línea de la OC que apuntaba a esa RQ.</item>
///   <item>Cancelar la OC sin recepciones (caso por línea).</item>
/// </list>
///
/// El listener <c>LineaRqLiberadaListener</c> consume el evento e invoca
/// <see cref="Domain.Requisicion.LiberarDeOc"/> para que la RQ vuelva al
/// pool. Idempotente: re-liberar una RQ ya libre es no-op.
///
/// <para>
/// El campo <c>CantidadLiberada</c> es para F5-PR4 (liberación parcial
/// cuando hay recepciones); en F4-PR3 siempre es <c>null</c> porque la
/// cancelación con recepciones parciales es otro flujo.
/// </para>
/// </summary>
public sealed record LineaRqLiberadaEvent(
    Guid RequisicionId,
    Guid OrdenCompraId,
    Guid EmpresaId,
    decimal? CantidadLiberada,
    DateTimeOffset OcurridoEn) : INotification;
