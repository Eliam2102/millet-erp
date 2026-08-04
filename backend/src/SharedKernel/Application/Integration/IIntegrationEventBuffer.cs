namespace Millet.SharedKernel.Application.Integration;

/// <summary>
/// Buffer scoped (por request / scope DI) que acumula eventos de
/// integración publicados durante el flujo. El interceptor de outbox
/// drena el buffer en <c>SavingChanges</c> e inserta filas a la tabla
/// <c>integration_events_outbox</c> dentro de la misma TX EF.
///
/// <para>
/// Lifetime: <b>scoped</b>. Una instancia por scope DI, compartida por
/// TODOS los DbContext del scope. El publisher
/// (<c>OutboxIntegrationEventPublisher</c>) y el interceptor
/// (<c>OutboxSaveChangesInterceptor</c>) reciben la misma instancia.
/// </para>
/// <para>
/// <b>Ruteo por schema (P9-H7):</b> al encolar, cada evento se etiqueta con
/// el schema del módulo dueño de su outbox (vía
/// <see cref="IntegrationEventSchemaRouting"/>). El interceptor drena SOLO
/// los eventos de su propio schema (<see cref="DrainForSchema"/>), de modo
/// que un <c>SaveChanges</c> de otro módulo ya no puede robarse el evento y
/// escribirlo en el outbox equivocado.
/// </para>
/// </summary>
public interface IIntegrationEventBuffer
{
    /// <summary>Encola un evento para que el interceptor lo persista en outbox.</summary>
    void Enqueue(IntegrationEvent integrationEvent);

    /// <summary>
    /// Devuelve y remueve los eventos pendientes cuyo módulo dueño mapea al
    /// <paramref name="schema"/> dado. Lo invoca el interceptor en
    /// <c>SavingChanges</c> del DbContext dueño de ese schema. Preserva el
    /// orden de encolado; si no hay coincidencias devuelve lista vacía.
    /// </summary>
    IReadOnlyList<IntegrationEvent> DrainForSchema(string schema);

    /// <summary>
    /// Devuelve y remueve los eventos <b>sin ruta de schema conocida</b>
    /// (módulos sin outbox propio: <c>admin.*</c>, <c>identidad.*</c>, o
    /// eventos de prueba). Fallback legacy: los absorbe el primer
    /// <c>SaveChanges</c> de la request, igual que antes de P9-H7.
    /// </summary>
    IReadOnlyList<IntegrationEvent> DrainUnrouted();
}
