using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Eventos;

/// <summary>
/// Marca de idempotencia para eventos de integración consumidos
/// cross-módulo (CXC-PR3). Réplica del patrón de CxP F5-PR1 / Almacén A12.
///
/// <para>
/// Cuando un listener procesa un evento, inserta una fila con
/// <c>(EventoId, EventoTipo)</c>. La constraint única garantiza que
/// re-entregas del Service Bus (at-least-once) no procesen dos veces.
/// </para>
/// </summary>
public sealed class EventoProcesado : BaseEntity
{
    /// <summary>ID único del evento (MessageId del integration event).</summary>
    public Guid EventoId { get; private set; }

    /// <summary>EventType canónico (ej. <c>facturacion.factura-venta.timbrada.v1</c>).</summary>
    public string EventoTipo { get; private set; } = default!;

    public DateTimeOffset ProcesadoEn { get; private set; }

    /// <summary>Diagnóstico opcional para auditoría / debug.</summary>
    public string? Detalle { get; private set; }

    private EventoProcesado() { }

    public EventoProcesado(Guid eventoId, string eventoTipo, DateTimeOffset procesadoEn, string? detalle = null)
        : base(Guid.CreateVersion7())
    {
        EventoId = eventoId;
        EventoTipo = eventoTipo;
        ProcesadoEn = procesadoEn;
        Detalle = detalle;
    }
}
