using System.Net;
using System.Text.Json;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.Auth;

/// <summary>
/// Registra los accesos denegados y los cambios de empresa en la bitácora
/// consolidada (el login exitoso no se audita para no ensuciarla). Nunca conserva credenciales, tokens ni correos.
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
        CancellationToken ct,
        string? actorNombre = null,
        string? actorEmail = null,
        string? resumen = null,
        string? ruta = null,
        string? permiso = null)
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
            ActorTipo = "usuario",
            ActorNombre = actorNombre ?? (userId.HasValue ? $"Usuario {userId.Value.ToString()[..8]}" : "Sistema"),
            ActorEmail = actorEmail,
            EntidadEtiqueta = "Sesión de usuario",
            Resumen = resumen ?? (operation == "acceso" ? "Inicio de sesión" : (operation.Contains("denegado") ? $"Acceso denegado ({reasonCode ?? "error"})" : operation)),
            Cambios = JsonSerializer.Serialize(new
            {
                canal = channel,
                resultado = operation.EndsWith("denegado", StringComparison.Ordinal) ? "denegado" : "permitido",
                motivo = reasonCode,
                ruta,
                permiso,
            }),
            Ip = ip,
            CorrelationId = Guid.CreateVersion7(),
            EsBulk = false,
        });
        await db.SaveChangesAsync(ct);
    }
}
