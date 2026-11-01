using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Api.Auth;

/// <summary>
/// Registra los accesos denegados, los cambios de empresa y el primer inicio de sesión exitoso del día por usuario en la
/// bitácora consolidada. Nunca conserva credenciales, tokens ni correos.
/// </summary>
public sealed class AuthAccessAuditWriter(CoreDbContext db, IClock clock)
{
    private static readonly TimeZoneInfo ZonaMexico = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    /// <summary>
    /// ADM-BIT: un registro de acceso por usuario y día (día de America/Mexico_City). Los siguientes inicios de sesión del
    /// mismo día no se registran, para no llenar la bitácora de ruido; los denegados siempre se registran.
    /// </summary>
    public async Task RegistrarPrimerAccesoDelDiaAsync(
        Guid userId, Guid? empresaId, string? actorNombre, IPAddress? ip, CancellationToken ct)
    {
        var local = TimeZoneInfo.ConvertTime(clock.UtcNow, ZonaMexico);
        var inicioDia = new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
        var yaEntro = await db.AuditLog.AnyAsync(a => a.UsuarioId == userId && a.Modulo == "Identidad"
            && a.Entidad == "Sesion" && a.Operacion == "acceso" && a.Timestamp >= inicioDia, ct);
        if (!yaEntro)
            await WriteAsync("acceso", userId, empresaId, "EntraId", null, ip, ct, actorNombre: actorNombre);
    }

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
