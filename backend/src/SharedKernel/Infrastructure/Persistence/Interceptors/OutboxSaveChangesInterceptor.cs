using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Millet.SharedKernel.Application.Integration;
using Millet.SharedKernel.Infrastructure.Outbox;

namespace Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Interceptor que drena el <see cref="IIntegrationEventBuffer"/> en
/// <c>SavingChanges</c> y agrega filas a la tabla
/// <c>integration_events_outbox</c> del DbContext que está guardando.
/// Atomicidad: las filas se insertan dentro de la misma TX EF que la
/// operación de negocio — si SaveChanges rollbackea, las filas no
/// quedan colgando (ADR-0009 + diseño §7.3).
///
/// <para>
/// El interceptor es genérico por DbContext: cada módulo que tenga
/// outbox propio (Compras en F6-PR1; otros módulos cuando integren)
/// debe (1) tener un <c>DbSet&lt;IntegrationEventOutboxEntry&gt;</c>
/// en su DbContext mapeado a su tabla de outbox, y (2) registrar este
/// interceptor en su lista de interceptors.
/// </para>
/// <para>
/// <b>Drenado por schema (P9-H7):</b> el buffer es scoped y compartido por
/// todos los DbContext de la request. Cada evento se etiquetó al encolar
/// con el schema de su módulo dueño; aquí el interceptor resuelve el schema
/// del DbContext que está guardando (desde su modelo EF) y drena SOLO los
/// eventos de ese schema. Así un SaveChanges de otro módulo ya no se lleva
/// eventos ajenos a un outbox equivocado (antes: el primer SaveChanges
/// drenaba TODO y los eventos de la NC de amortización terminaban en
/// <c>integraciones_fiscal</c> en vez de <c>facturacion</c>). Los eventos
/// sin ruta conocida (admin/identidad) conservan el fallback legacy: los
/// absorbe el primer SaveChanges de la request.
/// </para>
/// </summary>
public sealed class OutboxSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    private readonly IIntegrationEventBuffer _buffer;
    private readonly ILogger<OutboxSaveChangesInterceptor> _logger;

    public OutboxSaveChangesInterceptor(
        IIntegrationEventBuffer buffer,
        ILogger<OutboxSaveChangesInterceptor> logger)
    {
        _buffer = buffer;
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        DrainAndPersist(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        DrainAndPersist(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void DrainAndPersist(DbContext? context)
    {
        if (context is null) return;

        // Schema del outbox de ESTE DbContext (== HasDefaultSchema del módulo).
        // Si el contexto no mapea el outbox, no hay dónde persistir → no-op.
        var schema = context.Model
            .FindEntityType(typeof(IntegrationEventOutboxEntry))?
            .GetSchema();
        if (string.IsNullOrEmpty(schema)) return;

        // Solo los eventos ruteados a MI schema.
        var pending = new List<IntegrationEvent>(_buffer.DrainForSchema(schema));

        // Fallback legacy (P9-H7): eventos de módulos sin outbox propio
        // (admin/identidad) los absorbe el primer SaveChanges de la request,
        // exactamente como antes. Solo el primer contexto que guarda los ve.
        var unrouted = _buffer.DrainUnrouted();
        if (unrouted.Count > 0)
        {
            pending.AddRange(unrouted);
            // PLATFORM-TODO(<OutboxRutaAdminIdentidad>): admin.* e identidad.*
            // no tienen outbox/worker propios; se persisten en el schema del
            // primer SaveChanges (aquí '{Schema}') como best-effort. Cuando esos
            // módulos tengan su outbox, agregar su prefijo a
            // IntegrationEventSchemaRouting y remover este fallback.
            _logger.LogWarning(
                "Outbox: {Count} evento(s) de integración sin ruta de schema se persistieron en '{Schema}' " +
                "por fallback legacy (módulo sin outbox propio). EventTypes={EventTypes}",
                unrouted.Count, schema, string.Join(",", unrouted.Select(e => e.EventType)));
        }

        if (pending.Count == 0) return;

        var outboxSet = context.Set<IntegrationEventOutboxEntry>();
        foreach (var ev in pending)
        {
            // Serializa el evento concreto (no la base abstracta) para
            // que el consumer lo pueda deserializar con su tipo.
            var payload = JsonSerializer.Serialize(ev, ev.GetType(), JsonOptions);

            outboxSet.Add(new IntegrationEventOutboxEntry(
                id: Guid.CreateVersion7(),
                eventType: ev.EventType,
                payload: payload,
                occurredAt: ev.OcurridoEn,
                empresaId: ev.EmpresaId));
        }
    }
}
