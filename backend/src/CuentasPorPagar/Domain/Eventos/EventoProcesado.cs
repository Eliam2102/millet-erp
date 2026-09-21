using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Eventos;

/// <summary>
/// Marca de idempotencia para eventos de integración consumidos
/// cross-módulo (F5-PR1). Análogo a <c>almacen.eventos_procesados</c>
/// del A12 de Almacén §5.
///
/// <para>
/// Cuando un listener procesa un evento, inserta una fila con
/// <c>(EventoId, EventoTipo)</c>. La constraint única garantiza que
/// re-entregas del Service Bus (at-least-once) no procesen dos veces
/// — el segundo intento captura una <c>UniqueConstraintException</c>
/// y se ignora.
/// </para>
///
/// <para>
/// Marcada <see cref="INotAudited"/>: es ruido de tabla operativa (ADR-0008,
/// Capa 4, mismo caso que "eventos procesados" citado explícitamente como
/// ejemplo de exclusión) — cada fila solo confirma que un mensaje ya se
/// procesó, sin valor de negocio para auditoría.
/// </para>
/// </summary>
public sealed class EventoProcesado : BaseEntity, INotAudited
{
    /// <summary>ID único del evento (correlation_id del integration event).</summary>
    public Guid EventoId { get; private set; }

    /// <summary>EventType canónico (ej. <c>compras.orden-compra.autorizada.v1</c>).</summary>
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
