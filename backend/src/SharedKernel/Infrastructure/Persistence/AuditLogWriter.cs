using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.SharedKernel.Infrastructure.Persistence;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly CoreDbContext _db;
    private readonly IClock _clock;
    private readonly IAuditCorrelationContext _correlationContext;

    public AuditLogWriter(
        CoreDbContext db,
        IClock clock,
        IAuditCorrelationContext correlationContext)
    {
        _db = db;
        _clock = clock;
        _correlationContext = correlationContext;
    }

    public async Task RegistrarAsync(
        string operacion,
        string modulo,
        string entidad,
        Guid? entidadId,
        Guid? aggregateRootId,
        string actorNombre,
        string actorTipo,
        string? actorEmail,
        string entidadEtiqueta,
        string resumen,
        Guid? usuarioId = null,
        Guid? empresaId = null,
        string cambios = "{}",
        string? metadatos = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = _correlationContext.CorrelationId ?? Guid.CreateVersion7();

        var entry = new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            Timestamp = _clock.UtcNow,
            UsuarioId = usuarioId,
            EmpresaId = empresaId,
            Modulo = modulo,
            Entidad = entidad,
            EntidadId = entidadId,
            AggregateRootId = aggregateRootId ?? entidadId,
            Operacion = operacion,
            Cambios = string.IsNullOrWhiteSpace(cambios) ? "{}" : cambios,
            ActorNombre = actorNombre,
            ActorTipo = actorTipo,
            ActorEmail = actorEmail,
            EntidadEtiqueta = entidadEtiqueta,
            Resumen = resumen,
            CorrelationId = correlationId,
            EsBulk = false,
            Metadatos = metadatos
        };

        _db.AuditLog.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
