using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Auditoria;

/// <summary>
/// Query consolidada sobre el log de auditoría (<c>core.audit_log</c>,
/// ADR-0008). Cierre del <c>PLATFORM-TODO(&lt;AuditUI&gt;)</c> declarado en
/// 01-diseno §12 del módulo Administración.
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
/// Una fila del log de auditoría enriquecida para UI (con nombre de
/// usuario via JOIN cuando disponible).
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
    string Operacion,
    string Cambios,
    Guid CorrelationId,
    Guid? SucursalId,
    string? SucursalClave);

public sealed record ConsultarBitacoraResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Total);

public sealed class ConsultarBitacoraHandler
    : IRequestHandler<ConsultarBitacoraQuery, ConsultarBitacoraResponse>
{
    private readonly CoreDbContext _db;
    private readonly ICurrentEmpresaContext _empresaContext;
    private readonly CompartidoDbContext _compartidoDb;

    public ConsultarBitacoraHandler(CoreDbContext db, ICurrentEmpresaContext empresaContext,
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
        if (request.SucursalId is Guid sucursal)
        {
            var filtro = System.Text.Json.JsonSerializer.Serialize(new { sucursalId = sucursal });
            query = query.Where(a => a.Metadatos != null &&
                EF.Functions.JsonContains(a.Metadatos, filtro));
        }

        var total = await query.CountAsync(cancellationToken);

        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip(request.Offset)
            .Take(request.Limit)
            .Select(a => new AuditLogEntryResponse(
                a.Id,
                a.Timestamp,
                a.UsuarioId,
                null,  // UsuarioNombre — enriquecimiento cross-schema diferido; null hasta que se prioritice
                a.EmpresaId,
                a.Modulo,
                a.Entidad,
                a.EntidadId,
                a.Operacion,
                a.Cambios,
                a.CorrelationId,
                (Guid?)null,
                (string?)null))
            .ToListAsync(cancellationToken);

        // La proyección JSONB se hace después de paginar en SQL.
        var ids = rows.Select(r => r.Id).ToList();
        var metadatosPorId = await _db.AuditLog.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Metadatos })
            .ToDictionaryAsync(a => a.Id, a => a.Metadatos, cancellationToken);
        var sucursalIds = new HashSet<Guid>();
        rows = rows.Select(row =>
        {
            if (!metadatosPorId.TryGetValue(row.Id, out var json) || json is null)
                return row;
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sucursalId", out var sid) || !sid.TryGetGuid(out var value))
                return row;
            sucursalIds.Add(value);
            var clave = doc.RootElement.TryGetProperty("sucursalClave", out var claveJson)
                ? claveJson.GetString() : null;
            return row with { SucursalId = value, SucursalClave = clave };
        }).ToList();

        if (sucursalIds.Count > 0)
        {
            var claves = await _compartidoDb.Sucursales.AsNoTracking()
                .Where(s => sucursalIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Clave, cancellationToken);
            rows = rows.Select(r => r.SucursalId is Guid sid &&
                r.SucursalClave is null && claves.TryGetValue(sid, out var clave)
                ? r with { SucursalClave = clave } : r).ToList();
        }

        return new ConsultarBitacoraResponse(rows, total);
    }
}
