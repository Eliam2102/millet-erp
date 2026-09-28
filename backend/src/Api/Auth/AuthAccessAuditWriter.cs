using System.Net;
using System.Text.Json;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.Auth;

/// <summary>
/// Registra cada intento de inicio de sesión o cambio de empresa en la
/// bitácora consolidada. Nunca conserva credenciales, tokens ni correos.
/// </summary>
public sealed class AuthAccessAuditWriter(CoreDbContext db, IClock clock)
{
    public async Task WriteAsync(
        string operation,
        Guid? userId,
        Guid? empresaId,
        string channel,
        string? reasonCode,
        IPAddress? ip,
        CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntry
        {
            Id = Guid.CreateVersion7(),
            Timestamp = clock.UtcNow,
            UsuarioId = userId,
            EmpresaId = empresaId,
            Modulo = "Identidad",
            Entidad = "Sesion",
            EntidadId = userId,
            AggregateRootId = userId,
            Operacion = operation,
            Cambios = JsonSerializer.Serialize(new
            {
                canal = channel,
                resultado = operation.EndsWith("denegado", StringComparison.Ordinal) ? "denegado" : "permitido",
                motivo = reasonCode,
            }),
            Ip = ip,
            CorrelationId = Guid.CreateVersion7(),
            EsBulk = false,
        });
        await db.SaveChangesAsync(ct);
    }
}
