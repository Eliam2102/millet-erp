namespace Millet.Almacen.Domain.Idempotencia;

/// <summary>
/// Registro de idempotencia para consumidores de eventos cross-módulo
/// (A12 del 01-diseno, cuidado §2.2 del 04-cuidados-infra). Tabla
/// <c>almacen.eventos_procesados</c> con PK compuesto
/// <c>(evento_id, evento_tipo)</c>.
///
/// <para>
/// Cada listener verifica este registro ANTES de procesar; si ya existe,
/// logea idempotent-skip y descarta. El INSERT se hace dentro de la
/// misma TX que el efecto del evento (UPSERT atómico). Service Bus es
/// at-least-once: el mismo evento puede llegar dos veces; este registro
/// garantiza que el efecto se aplica una sola vez.
/// </para>
/// </summary>
public sealed class EventoProcesado
{
    public Guid EventoId { get; private set; }
    public string EventoTipo { get; private set; } = string.Empty;
    public DateTimeOffset ProcesadoAt { get; private set; }
    public string? Observaciones { get; private set; }

    private EventoProcesado() { }

    public EventoProcesado(Guid eventoId, string eventoTipo, string? observaciones = null)
    {
        if (eventoId == Guid.Empty)
            throw new ArgumentException("eventoId requerido.", nameof(eventoId));
        if (string.IsNullOrWhiteSpace(eventoTipo))
            throw new ArgumentException("eventoTipo requerido.", nameof(eventoTipo));

        EventoId = eventoId;
        EventoTipo = eventoTipo;
        ProcesadoAt = DateTimeOffset.UtcNow;
        Observaciones = observaciones;
    }
}
