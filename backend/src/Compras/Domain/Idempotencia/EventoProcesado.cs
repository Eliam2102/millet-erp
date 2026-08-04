namespace Millet.Compras.Domain.Idempotencia;

/// <summary>
/// Marca de idempotencia para eventos de integración consumidos por
/// listeners cross-módulo del módulo Compras. Análogo a
/// <c>almacen.eventos_procesados</c> y
/// <c>cuentas_por_pagar.eventos_procesados</c>.
///
/// <para>
/// PK compuesto <c>(evento_id, evento_tipo)</c> — un mismo GUID puede
/// aparecer en múltiples eventos de distinto tipo y queremos dedupe por
/// par. El listener consulta antes de despachar al handler; si ya
/// existe, completa el mensaje en Service Bus y descarta.
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
