using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.SharedKernel.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Captura los cambios de las entidades <see cref="IAuditable"/> y los
/// inserta como filas en <c>core.audit_log</c> dentro de la misma
/// transacción de SaveChanges. Garantiza atomicidad: entidades modificadas
/// y registros de auditoría son INSERT en la misma transacción de PostgreSQL.
///
/// Formato de <c>Cambios</c> (jsonb):
/// - Crear: snapshot completo de la entidad — <c>{"snapshot": {...}, "snapshotTexto": {...}}</c>
/// - Actualizar: diff campo a campo — <c>{"diff": {"campo": {"antes": ..., "despues": ..., "antesTexto": ..., "despuesTexto": ...}}}</c>
/// - Borrar: snapshot pre-borrado — <c>{"snapshot_pre_borrado": {...}, "snapshotTexto": {...}}</c>
///
/// Ver ADR-0008 y F1-ADM-03.
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IClock _clock;
    private readonly ICurrentUserContext _userContext;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly IAuditOriginContext _originContext;
    private readonly IAuditCorrelationContext _correlationContext;

    public AuditSaveChangesInterceptor(
        IClock clock,
        ICurrentUserContext userContext,
        ICurrentEmpresaContext empresaContext,
        IAuditOriginContext originContext,
        IAuditCorrelationContext correlationContext)
    {
        _clock = clock;
        _userContext = userContext;
        _empresaContext = empresaContext;
        _originContext = originContext;
        _correlationContext = correlationContext;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null) AddAuditEntries(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null) AddAuditEntries(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void AddAuditEntries(DbContext context)
    {
        var auditEntries = new List<AuditLogEntry>();
        var now = _clock.UtcNow;
        // Una operación de varios SaveChanges puede fijar correlación común.
        var correlationId = _correlationContext.CorrelationId ?? Guid.CreateVersion7();
        // Solo se popula Metadatos.origen cuando el bypass de empresa está
        // activo (procesos en background) y el worker declaró su origen con
        // IAuditOriginContext.SetOrigin — en request normales ambos son null.
        var origen = _empresaContext.IsBypassed ? _originContext.Origin : null;

        // Snapshot la lista ahora porque la voy a modificar (agrego AuditLogEntry).
        var trackedEntries = context.ChangeTracker.Entries().ToList();

        foreach (var entry in trackedEntries)
        {
            if (entry.Entity is not IAuditable) continue;
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;

            // Si es modificacion y solo cambiaron campos tecnicos o sensibles, no escribir fila
            if (entry.State == EntityState.Modified)
            {
                var hasBusinessChanges = entry.Properties.Any(p => p.IsModified && IsAuditableProperty(p));
                if (!hasBusinessChanges) continue;
            }

            var entityId = (entry.Entity as BaseEntity)?.Id;
            // B.2: AggregateRootId = RootId si la entidad implementa
            // IBelongsToAggregate; sino el propio Id (la entidad ES root
            // por convención). Permite consultar el histórico de un
            // agregado completo (root + hijos) con un solo WHERE.
            var aggregateRootId = entry.Entity is IBelongsToAggregate aggregate
                ? aggregate.AggregateRootId
                : entityId;

            // La sucursal se toma del recurso auditado, no de una selección
            // visual del navegador: así el log conserva su significado aun
            // cuando la operación se ejecute desde un worker o una API.
            var sucursalProperty = entry.Properties.FirstOrDefault(p =>
                p.Metadata.Name == "SucursalId");
            var sucursalId = sucursalProperty?.CurrentValue as Guid?
                ?? sucursalProperty?.OriginalValue as Guid?;
            if (sucursalId is null && entry.Entity.GetType().Name == "Sucursal")
                sucursalId = entityId;

            var metadata = new Dictionary<string, object>();
            if (origen is not null) metadata["origen"] = origen;
            if (sucursalId is Guid sid) metadata["sucursalId"] = sid;
            if (entry.Entity.GetType().Name == "Sucursal")
            {
                var clave = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Clave")?.CurrentValue as string;
                if (!string.IsNullOrWhiteSpace(clave)) metadata["sucursalClave"] = clave;
            }

            // Actor
            string actorTipo;
            string actorNombre;
            string? actorEmail;

            if (_userContext.UserId.HasValue && _userContext.UserId.Value != Guid.Empty)
            {
                actorTipo = "usuario";
                actorNombre = !string.IsNullOrWhiteSpace(_userContext.UserName) ? _userContext.UserName : "Usuario";
                actorEmail = _userContext.Email;
            }
            else if (_empresaContext.IsBypassed && !string.IsNullOrWhiteSpace(origen))
            {
                actorTipo = "proceso";
                actorNombre = $"Proceso: {origen}";
                actorEmail = null;
            }
            else
            {
                actorTipo = "sistema";
                actorNombre = "Sistema";
                actorEmail = null;
            }

            var entidad = entry.Entity.GetType().Name;
            var entidadEtiqueta = ResolveEntidadEtiqueta(entry, entityId);
            var (cambiosJson, changedBusinessProps) = SerializeChanges(entry);
            var resumen = ResolveResumen(entry, entidad, entidadEtiqueta, changedBusinessProps);

            var auditEntry = new AuditLogEntry
            {
                Id = Guid.CreateVersion7(),
                Timestamp = now,
                UsuarioId = _userContext.UserId,
                // El bypass permite operar entre empresas, pero no convierte
                // una entidad de una empresa en un evento global. La empresa
                // del recurso auditado prevalece sobre el contexto del actor.
                EmpresaId = entry.Entity is IPerteneceAEmpresa scoped
                    ? scoped.EmpresaId
                    : (_empresaContext.IsBypassed ? null : _empresaContext.Current),
                Modulo = ResolveModule(entry.Entity.GetType()),
                Entidad = entidad,
                EntidadId = entityId,
                AggregateRootId = aggregateRootId,
                Operacion = entry.State switch
                {
                    EntityState.Added => "crear",
                    EntityState.Modified => "actualizar",
                    EntityState.Deleted => "borrar",
                    _ => "unknown"
                },
                Cambios = cambiosJson,
                ActorNombre = actorNombre,
                ActorTipo = actorTipo,
                ActorEmail = actorEmail,
                EntidadEtiqueta = entidadEtiqueta,
                Resumen = resumen,
                CorrelationId = correlationId,
                EsBulk = false,
                Metadatos = metadata.Count > 0 ? JsonSerializer.Serialize(metadata) : null
            };

            auditEntries.Add(auditEntry);
        }

        if (auditEntries.Count > 0)
        {
            context.Set<AuditLogEntry>().AddRange(auditEntries);
        }
    }

    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "ContrasenaTemporal",
        "Contrasena",
        "Password"
    };

    private static readonly HashSet<string> TechnicalProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Version",
        "CreatedAt",
        "CreatedBy",
        "UpdatedAt",
        "UpdatedBy",
        "RowVersion",
        "xmin",
        "PrimerAccesoEn"
    };

    private static bool IsAuditableProperty(PropertyEntry p) =>
        !SensitiveProperties.Contains(p.Metadata.Name) &&
        !TechnicalProperties.Contains(p.Metadata.Name);

    private static string ResolveEntidadEtiqueta(EntityEntry entry, Guid? entityId)
    {
        string? GetPropValue(string propName)
        {
            var p = entry.Properties.FirstOrDefault(x => string.Equals(x.Metadata.Name, propName, StringComparison.OrdinalIgnoreCase));
            if (p is null) return null;
            var val = (entry.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue) ?? p.OriginalValue;
            var str = val?.ToString()?.Trim();
            return string.IsNullOrEmpty(str) ? null : str;
        }

        string? code = null;
        foreach (var name in new[] { "Clave", "Folio", "Codigo", "NumeroEmpleado" })
        {
            code = GetPropValue(name);
            if (code is not null) break;
        }

        string? textName = null;
        foreach (var name in new[] { "Nombre", "NombreCompleto", "RazonSocial" })
        {
            textName = GetPropValue(name);
            if (textName is not null) break;
        }

        string? contact = null;
        foreach (var name in new[] { "Email", "Rfc" })
        {
            contact = GetPropValue(name);
            if (contact is not null) break;
        }

        if (code is not null && textName is not null)
            return $"{code} · {textName}";
        if (code is not null)
            return code;
        if (textName is not null)
            return textName;
        if (contact is not null)
            return contact;

        var idPrefix = entityId?.ToString();
        var shortId = !string.IsNullOrEmpty(idPrefix) && idPrefix.Length >= 8 ? idPrefix[..8] : (idPrefix ?? "00000000");
        return $"{entry.Entity.GetType().Name} {shortId}";
    }

    private static string ResolveResumen(
        EntityEntry entry,
        string entidad,
        string etiqueta,
        List<(string PropertyName, string? AntesTexto, string? DespuesTexto, object? AntesRaw, object? DespuesRaw)> changedBusinessProps)
    {
        var verbo = entry.State switch
        {
            EntityState.Added => "Creó",
            EntityState.Modified => "Modificó",
            EntityState.Deleted => "Eliminó",
            _ => entry.State.ToString()
        };

        var baseResumen = $"{verbo} {entidad} {etiqueta}".Trim();

        if (entry.State == EntityState.Modified && changedBusinessProps.Count > 0)
        {
            var details = changedBusinessProps.Take(3).Select(c =>
            {
                var antes = c.AntesTexto ?? c.AntesRaw?.ToString() ?? "null";
                var despues = c.DespuesTexto ?? c.DespuesRaw?.ToString() ?? "null";
                return $"{c.PropertyName}: {antes} → {despues}";
            });

            var suffix = string.Join(", ", details);
            if (changedBusinessProps.Count > 3)
            {
                suffix += $" y {changedBusinessProps.Count - 3} más";
            }

            return $"{baseResumen} — {suffix}";
        }

        return baseResumen;
    }

    private static string? FormatValueText(PropertyEntry p, object? value, EntityEntry entry)
    {
        if (value is null) return null;

        var clrType = p.Metadata.ClrType;
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (underlying == typeof(bool))
        {
            return (bool)value ? "Sí" : "No";
        }

        if (underlying.IsEnum)
        {
            return Enum.GetName(underlying, value) ?? value.ToString();
        }

        try
        {
            var nav = entry.Navigations.FirstOrDefault(n =>
                n.Metadata is Microsoft.EntityFrameworkCore.Metadata.INavigation navigation &&
                navigation.ForeignKey.Properties.Contains(p.Metadata));

            if (nav?.IsLoaded == true && nav.CurrentValue is not null)
            {
                var relatedEntry = entry.Context.Entry(nav.CurrentValue);
                var relatedId = (relatedEntry.Entity as BaseEntity)?.Id;
                return ResolveEntidadEtiqueta(relatedEntry, relatedId);
            }
        }
        catch
        {
            // Tolerar en mocks o contextos in-memory sin soporte de navigations completas
        }

        return null;
    }

    private static (string Json, List<(string PropertyName, string? AntesTexto, string? DespuesTexto, object? AntesRaw, object? DespuesRaw)> ChangedBusinessProps) SerializeChanges(EntityEntry entry)
    {
        var changedProps = new List<(string PropertyName, string? AntesTexto, string? DespuesTexto, object? AntesRaw, object? DespuesRaw)>();

        switch (entry.State)
        {
            case EntityState.Added:
            {
                var snapshot = new Dictionary<string, object?>();
                var snapshotTexto = new Dictionary<string, string>();
                foreach (var p in entry.Properties.Where(IsAuditableProperty))
                {
                    snapshot[p.Metadata.Name] = p.CurrentValue;
                    var text = FormatValueText(p, p.CurrentValue, entry);
                    if (text is not null)
                    {
                        snapshotTexto[p.Metadata.Name] = text;
                    }
                }

                object resultObj = snapshotTexto.Count > 0
                    ? new { snapshot, snapshotTexto }
                    : new { snapshot };

                return (JsonSerializer.Serialize(resultObj), changedProps);
            }

            case EntityState.Modified:
            {
                var diff = new Dictionary<string, object>();
                foreach (var p in entry.Properties.Where(p => p.IsModified && IsAuditableProperty(p)))
                {
                    var antesTexto = FormatValueText(p, p.OriginalValue, entry);
                    var despuesTexto = FormatValueText(p, p.CurrentValue, entry);

                    changedProps.Add((p.Metadata.Name, antesTexto, despuesTexto, p.OriginalValue, p.CurrentValue));

                    var item = new Dictionary<string, object?>
                    {
                        ["antes"] = p.OriginalValue,
                        ["despues"] = p.CurrentValue
                    };

                    if (antesTexto is not null) item["antesTexto"] = antesTexto;
                    if (despuesTexto is not null) item["despuesTexto"] = despuesTexto;

                    diff[p.Metadata.Name] = item;
                }

                return (JsonSerializer.Serialize(new { diff }), changedProps);
            }

            case EntityState.Deleted:
            {
                var snapshotPreBorrado = new Dictionary<string, object?>();
                var snapshotTexto = new Dictionary<string, string>();
                foreach (var p in entry.Properties.Where(IsAuditableProperty))
                {
                    snapshotPreBorrado[p.Metadata.Name] = p.OriginalValue;
                    var text = FormatValueText(p, p.OriginalValue, entry);
                    if (text is not null)
                    {
                        snapshotTexto[p.Metadata.Name] = text;
                    }
                }

                object resultObj = snapshotTexto.Count > 0
                    ? new { snapshot_pre_borrado = snapshotPreBorrado, snapshotTexto }
                    : new { snapshot_pre_borrado = snapshotPreBorrado };

                return (JsonSerializer.Serialize(resultObj), changedProps);
            }

            default:
                return ("{}", changedProps);
        }
    }

    private static string ResolveModule(Type entityType)
    {
        var ns = entityType.Namespace ?? string.Empty;
        var parts = ns.Split(".");
        return parts.Length >= 2 && parts[0] == "Millet" ? parts[1] : "Unknown";
    }
}
