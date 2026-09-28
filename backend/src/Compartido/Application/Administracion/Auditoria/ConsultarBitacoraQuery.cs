using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Auditoria;

/// <summary>
/// Query consolidada sobre el log de auditoría (<c>core.audit_log</c>,
/// ADR-0008). Cierre del <c>PLATFORM-TODO(&lt;AuditUI&gt;)</c> y F1-ADM-03.
///
/// <para>
/// El rango de fechas (<see cref="Desde"/>, <see cref="Hasta"/>) es
/// obligatorio para evitar table scans completos. Rango máximo: 90 días.
/// Validador del command lo enforce → 400 ProblemDetails con detalle.
/// </para>
/// </summary>
public sealed record ConsultarBitacoraQuery(
    DateOnly Desde,
    DateOnly Hasta,
    string? Modulo = null,
    string? Recurso = null,
    string? Accion = null,
    Guid? UsuarioId = null,
    Guid? EmpresaId = null,
    Guid? SucursalId = null,
    Guid? EntidadId = null,
    Guid? AggregateRootId = null,
    string? ActorTipo = null,
    string? Q = null,
    int Offset = 0,
    int Limit = 50,
    string? ZonaHoraria = null) : IRequest<ConsultarBitacoraResponse>;

public sealed class ConsultarBitacoraQueryValidator : AbstractValidator<ConsultarBitacoraQuery>
{
    public ConsultarBitacoraQueryValidator()
    {
        RuleFor(x => x.Desde)
            .Must((q, _) => q.Desde <= q.Hasta)
            .WithMessage("'desde' debe ser menor o igual que 'hasta'.");

        RuleFor(x => x.Hasta)
            .Must((q, _) => (q.Hasta.ToDateTime(TimeOnly.MinValue) - q.Desde.ToDateTime(TimeOnly.MinValue)).TotalDays <= 90)
            .WithMessage("El rango máximo permitido es 90 días (Hasta - Desde).");

        RuleFor(x => x.Offset).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, 200);
        RuleFor(x => x.ZonaHoraria)
            .Must(zona => zona is null || EsZonaHorariaValida(zona))
            .WithMessage("La zona horaria debe ser un identificador IANA válido.");
    }

    private static bool EsZonaHorariaValida(string zona)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(zona); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}

/// <summary>
/// Una fila del log de auditoría enriquecida para UI y reportes.
/// Proyecta las columnas snapshot (ActorNombre, ActorTipo, EntidadEtiqueta, Resumen)
/// y mantiene UsuarioNombre por compatibilidad con el frontend.
/// </summary>
public sealed record AuditLogEntryResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    Guid? UsuarioId,
    string? UsuarioNombre,
    Guid? EmpresaId,
    string Modulo,
    string Entidad,
    Guid? EntidadId,
    Guid? AggregateRootId,
    string Operacion,
    string Cambios,
    Guid CorrelationId,
    Guid? SucursalId,
    string? SucursalClave,
    string ActorNombre,
    string ActorTipo,
    string? ActorEmail,
    string EntidadEtiqueta,
    string Resumen,
    string? Origen);

public sealed record ConsultarBitacoraResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Total);

public sealed class ConsultarBitacoraHandler
    : IRequestHandler<ConsultarBitacoraQuery, ConsultarBitacoraResponse>
{
    private readonly CoreDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly CompartidoDbContext _compartidoDb;

    public ConsultarBitacoraHandler(
        CoreDbContext db,
        ICurrentEmpresaContext empresaContext,
        CompartidoDbContext compartidoDb)
    {
        _db = db;
        _empresaContext = empresaContext;
        _compartidoDb = compartidoDb;
    }

    public async Task<ConsultarBitacoraResponse> Handle(
        ConsultarBitacoraQuery request,
        CancellationToken cancellationToken)
    {
        // Las fechas del filtro representan días locales de quien consulta.
        // Sin zona horaria explícita se conserva el comportamiento UTC anterior.
        var zona = request.ZonaHoraria is null
            ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(request.ZonaHoraria);
        var desdeUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            request.Desde.ToDateTime(TimeOnly.MinValue), zona));
        var hastaUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            request.Hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), zona));

        var query = _db.AuditLog
            .AsNoTracking()
            .Where(a => a.Timestamp >= desdeUtc && a.Timestamp < hastaUtc);

        // La bitácora no tiene query filter global. Un evento sin empresa
        // puede pertenecer a una identidad de otra razón social; no se debe
        // exponer en la vista de una empresa por el solo hecho de ser global.
        if (_empresaContext.Current is Guid empresaActual)
            query = query.Where(a => a.EmpresaId == empresaActual);
        else
            query = query.Where(a => false);

        if (request.Modulo is { Length: > 0 })
            query = query.Where(a => a.Modulo == request.Modulo);
        if (request.Recurso is { Length: > 0 })
            query = query.Where(a => a.Entidad == request.Recurso);
        if (request.Accion is { Length: > 0 })
            query = query.Where(a => a.Operacion == request.Accion);
        if (request.UsuarioId is Guid u)
            query = query.Where(a => a.UsuarioId == u);
        if (request.EmpresaId is Guid e)
            query = query.Where(a => a.EmpresaId == e);
        if (request.EntidadId is Guid entId)
            query = query.Where(a => a.EntidadId == entId);
        if (request.AggregateRootId is Guid rootId)
            query = query.Where(a => a.AggregateRootId == rootId);
        if (request.ActorTipo is { Length: > 0 })
            query = query.Where(a => a.ActorTipo == request.ActorTipo);
        if (request.Q is { Length: > 0 } search)
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(a =>
                EF.Functions.ILike(a.ActorNombre, pattern) ||
                EF.Functions.ILike(a.EntidadEtiqueta, pattern) ||
                EF.Functions.ILike(a.Resumen, pattern));
        }
        if (request.SucursalId is Guid sucursal)
        {
            var filtro = System.Text.Json.JsonSerializer.Serialize(new { sucursalId = sucursal });
            query = query.Where(a => a.Metadatos != null &&
                EF.Functions.JsonContains(a.Metadatos, filtro));
        }

        var total = await query.CountAsync(cancellationToken);

        var rawRows = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.UsuarioId,
                a.ActorNombre,
                a.ActorTipo,
                a.ActorEmail,
                a.EntidadEtiqueta,
                a.Resumen,
                a.EmpresaId,
                a.Modulo,
                a.Entidad,
                a.EntidadId,
                a.AggregateRootId,
                a.Operacion,
                a.Cambios,
                a.CorrelationId,
                a.Metadatos
            })
            .ToListAsync(cancellationToken);


        var sucursalIds = new HashSet<Guid>();
        var items = new List<AuditLogEntryResponse>(rawRows.Count);

        foreach (var r in rawRows)
        {
            Guid? sucursalId = null;
            string? sucursalClave = null;
            string? origen = null;

            if (!string.IsNullOrWhiteSpace(r.Metadatos))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(r.Metadatos);
                    if (doc.RootElement.TryGetProperty("sucursalId", out var sid) && sid.TryGetGuid(out var val))
                    {
                        sucursalId = val;
                        sucursalIds.Add(val);
                    }
                    if (doc.RootElement.TryGetProperty("sucursalClave", out var sc) && sc.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        sucursalClave = sc.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("origen", out var orig) && orig.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        origen = orig.GetString();
                    }
                }
                catch
                {
                    // Ignorar json malformado en metadatos
                }
            }

            var actorNombre = !string.IsNullOrWhiteSpace(r.ActorNombre)
                ? r.ActorNombre
                : (r.UsuarioId.HasValue ? $"Usuario {r.UsuarioId.Value.ToString()[..8]}" : "Sistema");
            var actorTipo = !string.IsNullOrWhiteSpace(r.ActorTipo) ? r.ActorTipo : "usuario";
            var entidadEtiqueta = !string.IsNullOrWhiteSpace(r.EntidadEtiqueta)
                ? r.EntidadEtiqueta
                : $"{r.Entidad} {(r.EntidadId.HasValue ? r.EntidadId.Value.ToString()[..8] : string.Empty)}".Trim();
            var resumen = !string.IsNullOrWhiteSpace(r.Resumen)
                ? r.Resumen
                : $"{r.Operacion} {r.Entidad}";

            items.Add(new AuditLogEntryResponse(
                Id: r.Id,
                Timestamp: r.Timestamp,
                UsuarioId: r.UsuarioId,
                UsuarioNombre: actorNombre,
                EmpresaId: r.EmpresaId,
                Modulo: r.Modulo,
                Entidad: r.Entidad,
                EntidadId: r.EntidadId,
                AggregateRootId: r.AggregateRootId,
                Operacion: r.Operacion,
                Cambios: r.Cambios,
                CorrelationId: r.CorrelationId,
                SucursalId: sucursalId,
                SucursalClave: sucursalClave,
                ActorNombre: actorNombre,
                ActorTipo: actorTipo,
                ActorEmail: r.ActorEmail,
                EntidadEtiqueta: entidadEtiqueta,
                Resumen: resumen,
                Origen: origen));
        }

        if (sucursalIds.Count > 0)
        {
            var claves = await _compartidoDb.Sucursales.AsNoTracking()
                .Where(s => sucursalIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Clave, cancellationToken);

            items = items.Select(r => r.SucursalId is Guid sid &&
                r.SucursalClave is null && claves.TryGetValue(sid, out var clave)
                ? r with { SucursalClave = clave } : r).ToList();
        }

        return new ConsultarBitacoraResponse(items, total);
    }
}
