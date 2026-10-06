using System.Text.Json;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.Contabilidad.Application.Periodos;

/// <summary>Historial funcional en core.audit_log, junto al cambio del periodo en el mismo SaveChanges y transacción.</summary>
internal static class AuditoriaPeriodos
{
    public static void Registrar(ContabilidadDbContext db, PeriodoContable periodo, TransicionPeriodoContable transicion,
        ICurrentUserContext usuario, IAuditCorrelationContext correlacion)
    {
        // IAuditLogWriter guarda con CoreDbContext; aquí usamos el mapeo central de BaseDbContext para mantener atomicidad.
        var accion = transicion.Accion switch
        {
            AccionPeriodo.Abrir => "abrir",
            AccionPeriodo.Cerrar => "cerrar",
            AccionPeriodo.Reabrir => "reabrir",
            _ => throw new ArgumentOutOfRangeException(nameof(transicion))
        };
        db.Set<AuditLogEntry>().Add(new AuditLogEntry
        {
            Id = transicion.Id,
            Timestamp = transicion.OcurridoEn,
            EmpresaId = periodo.EmpresaId,
            UsuarioId = transicion.UsuarioId,
            Modulo = "Contabilidad",
            Entidad = nameof(PeriodoContable),
            EntidadId = periodo.Id,
            AggregateRootId = periodo.Id,
            Operacion = accion,
            ActorNombre = transicion.UsuarioNombre.Length > 128 ? transicion.UsuarioNombre[..128] : transicion.UsuarioNombre,
            ActorTipo = usuario.UserId.HasValue ? "usuario" : "sistema",
            ActorEmail = usuario.Email is { Length: > 256 } email ? email[..256] : usuario.Email,
            EntidadEtiqueta = periodo.Clave,
            Resumen = $"{transicion.Accion switch { AccionPeriodo.Abrir => "Abrió", AccionPeriodo.Cerrar => "Cerró", _ => "Reabrió" }} periodo {periodo.Clave}",
            CorrelationId = correlacion.CorrelationId ?? Guid.CreateVersion7(),
            Cambios = JsonSerializer.Serialize(new
            {
                diff = new
                {
                    Estado = new { antes = transicion.EstadoAnterior, despues = transicion.EstadoNuevo },
                    Motivo = new { antes = (string?)null, despues = transicion.Motivo },
                    VersionResultante = new { antes = periodo.Version, despues = transicion.VersionResultante }
                }
            }),
            Metadatos = JsonSerializer.Serialize(transicion)
        });
    }
}
