using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Infrastructure.Persistence;

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
    int Offset = 0,
    int Limit = 50) : IRequest<ConsultarBitacoraResponse>;

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
    Guid CorrelationId);

public sealed record ConsultarBitacoraResponse(
    IReadOnlyList<AuditLogEntryResponse> Items,
    int Total);

public sealed class ConsultarBitacoraHandler
    : IRequestHandler<ConsultarBitacoraQuery, ConsultarBitacoraResponse>
{
    private readonly CoreDbContext _db;

    public ConsultarBitacoraHandler(CoreDbContext db)
    {
        _db = db;
    }

    public async Task<ConsultarBitacoraResponse> Handle(
        ConsultarBitacoraQuery request,
        CancellationToken cancellationToken)
    {
        // Convertir DateOnly a DateTimeOffset UTC. La columna Timestamp en
        // PG es timestamptz; comparamos contra el rango [Desde 00:00 UTC,
        // (Hasta+1) 00:00 UTC) — incluye todo el día Hasta.
        var desdeUtc = new DateTimeOffset(request.Desde.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hastaUtc = new DateTimeOffset(request.Hasta.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var query = _db.AuditLog
            .AsNoTracking()
            .Where(a => a.Timestamp >= desdeUtc && a.Timestamp < hastaUtc);

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
                a.CorrelationId))
            .ToListAsync(cancellationToken);

        return new ConsultarBitacoraResponse(rows, total);
    }
}
