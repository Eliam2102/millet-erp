using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Eventos;

/// <summary>
/// Marca de idempotencia para eventos de integración consumidos
/// cross-módulo (réplica del patrón CxP F5-PR1 / CxC CXC-PR3). Los 3
/// listeners del módulo (CxP en PR-3, CxC y Facturación en PR-7)
/// insertan una fila por <c>(EventoId, EventoTipo)</c>; la constraint
/// única garantiza que re-entregas at-least-once del Service Bus no
/// procesen dos veces (cuidados-infra §2.3).
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
    /// <summary>ID único del evento (MessageId del integration event).</summary>
    public Guid EventoId { get; private set; }

    /// <summary>EventType canónico (ej. <c>cuentas_por_pagar.pasivo.autorizado-para-pago.v1</c>).</summary>
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
