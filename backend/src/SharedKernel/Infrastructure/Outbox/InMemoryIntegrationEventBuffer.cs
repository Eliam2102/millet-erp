using Millet.SharedKernel.Application.Integration;

namespace Millet.SharedKernel.Infrastructure.Outbox;

/// <summary>
/// Implementación scoped (in-memory list) de <see cref="IIntegrationEventBuffer"/>.
/// No es thread-safe — cada scope DI tiene su propia instancia y los
/// flujos dentro de un mismo scope son secuenciales (handlers de
/// MediatR + EF SaveChanges).
///
/// <para>
/// Cada evento se etiqueta al encolar con el schema del módulo dueño de su
/// outbox (<see cref="IntegrationEventSchemaRouting"/>). El drenado es por
/// schema para que el interceptor de cada DbContext se lleve SOLO lo suyo
/// (P9-H7); los eventos sin ruta conocida quedan aparte para el fallback
/// legacy.
/// </para>
/// </summary>
public sealed class InMemoryIntegrationEventBuffer : IIntegrationEventBuffer
{
    // Schema == null → evento sin módulo-dueño conocido (admin/identidad/test).
    private readonly List<(IntegrationEvent Event, string? Schema)> _events = [];

    public void Enqueue(IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var schema = IntegrationEventSchemaRouting.TryResolveSchema(integrationEvent.EventType, out var s)
            ? s
            : null;
        _events.Add((integrationEvent, schema));
    }

    public IReadOnlyList<IntegrationEvent> DrainForSchema(string schema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        return DrainWhere(e => string.Equals(e.Schema, schema, StringComparison.Ordinal));
    }

    public IReadOnlyList<IntegrationEvent> DrainUnrouted()
        => DrainWhere(e => e.Schema is null);

    private IReadOnlyList<IntegrationEvent> DrainWhere(
        Func<(IntegrationEvent Event, string? Schema), bool> predicate)
    {
        if (_events.Count == 0) return Array.Empty<IntegrationEvent>();

        var matched = new List<IntegrationEvent>();
        // Recorre de atrás hacia adelante para poder remover in-place;
        // luego revierte para restaurar el orden de encolado (FIFO).
        for (var i = _events.Count - 1; i >= 0; i--)
        {
            if (!predicate(_events[i])) continue;
            matched.Add(_events[i].Event);
            _events.RemoveAt(i);
        }

        if (matched.Count == 0) return Array.Empty<IntegrationEvent>();
        matched.Reverse();
        return matched;
    }
}
