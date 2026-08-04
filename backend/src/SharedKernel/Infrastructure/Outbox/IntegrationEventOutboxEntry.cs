using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Fila de la tabla <c>integration_events_outbox</c> (F6-PR1, ADR-0009).
/// Cada evento de integración publicado se persiste como una fila aquí
/// dentro de la TX EF que corresponde a la operación de negocio que lo
/// originó — atomicidad: si la operación rollbackea, el evento no
/// queda colgando.
///
/// <para>
/// El worker publisher (F6-PR2) lee filas con <c>PublishedAt IS NULL</c>
/// (índice parcial), las publica a Service Bus, y marca
/// <see cref="PublishedAt"/>. Reintentos incrementan
/// <see cref="Attempts"/>; tras 10 intentos fallidos se considera
/// dead-letter (F6-PR2 maneja la lógica).
/// </para>
/// <para>
/// La columna <see cref="Payload"/> guarda el JSON serializado del
/// <c>IntegrationEvent</c> concreto (incluido el tipo via discriminador).
/// </para>
/// </summary>
public sealed class IntegrationEventOutboxEntry : BaseEntity
{
    /// <summary>
    /// Clave estable del evento — convención
    /// <c>módulo.recurso.acción.vN</c> (ej. <c>compras.requisicion.autorizada.v1</c>).
    /// </summary>
    public string EventType { get; private set; } = default!;

    /// <summary>JSON serializado del evento concreto. Persiste como <c>jsonb</c> en Postgres.</summary>
    public string Payload { get; private set; } = default!;

    /// <summary>
    /// Marca temporal del evento (proveniente del agregado, no del row
    /// timestamp). El consumer la usa para ordenar / reproducir.
    /// </summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>EmpresaId del evento — denormalizado para queries multi-tenant.</summary>
    public Guid IntegrationEmpresaId { get; private set; }

    /// <summary>
    /// Marca de publicación al broker. <c>NULL</c> hasta que el worker
    /// la publica. El índice parcial <c>WHERE published_at IS NULL</c>
    /// hace el polling rápido aunque la tabla tenga millones de filas.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Contador de intentos (0 al insertar). Lo incrementa el worker en cada fallo.</summary>
    public int Attempts { get; private set; }

    /// <summary>Último error de publicación, NULL si nunca falló o ya se publicó OK.</summary>
    public string? LastError { get; private set; }

    private IntegrationEventOutboxEntry() { } // EF

    public IntegrationEventOutboxEntry(
        Guid id,
        string eventType,
        string payload,
        DateTimeOffset occurredAt,
        Guid empresaId) : base(id)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("eventType requerido", nameof(eventType));
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("payload requerido", nameof(payload));

        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
        IntegrationEmpresaId = empresaId;
    }

    /// <summary>
    /// Marca la fila como publicada (F6-PR2). El worker invoca este
    /// método tras un push exitoso a Service Bus.
    /// </summary>
    public void MarcarPublicado(DateTimeOffset publishedAt)
    {
        PublishedAt = publishedAt;
        LastError = null;
    }

    /// <summary>Registra un intento fallido (F6-PR2).</summary>
    public void RegistrarFalla(string error)
    {
        Attempts += 1;
        LastError = error;
    }
}
