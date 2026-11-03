using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Audit;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

// ============================================================================
// C7.2 (ClickUp 86e3kdxy6): administración de las reglas de serie. Las
// series son configuración: CxC da de alta, cambia el comportamiento y
// activa/desactiva; no se borran (una regla inactiva deja que la serie
// evalúe crédito). Cada cambio queda en core.audit_log por IAuditable y
// aplica a la siguiente evaluación (DecidirLiberacion lee la tabla en cada
// decisión, sin caché).
// ============================================================================

public sealed record ReglaLiberacionSerieAdminResponse(
    Guid Id,
    string Prefijo,
    ComportamientoSerie Comportamiento,
    bool Activo,
    int Version,
    DateTimeOffset ActualizadoEn,
    string? ActualizadoPor);

internal static class ReglaLiberacionSerieMapper
{
    public static ReglaLiberacionSerieAdminResponse ToResponse(ReglaLiberacionSerie r) =>
        new(r.Id, r.Prefijo, r.Comportamiento, r.Activo, r.Version, r.UpdatedAt, r.UpdatedBy);
}

// --------------------------------------------------- Listar (todas)

public sealed record ListarReglasLiberacionSerieAdminQuery : IRequest<IReadOnlyList<ReglaLiberacionSerieAdminResponse>>;

public sealed class ListarReglasLiberacionSerieAdminHandler
    : IRequestHandler<ListarReglasLiberacionSerieAdminQuery, IReadOnlyList<ReglaLiberacionSerieAdminResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarReglasLiberacionSerieAdminHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ReglaLiberacionSerieAdminResponse>> Handle(
        ListarReglasLiberacionSerieAdminQuery query, CancellationToken cancellationToken)
    {
        var reglas = await _db.ReglasLiberacionSerie.AsNoTracking()
            .OrderBy(r => r.Prefijo)
            .ToListAsync(cancellationToken);
        return reglas.Select(ReglaLiberacionSerieMapper.ToResponse).ToList();
    }
}

// --------------------------------------------------- Crear

public sealed record CrearReglaLiberacionSerieCommand(string Prefijo, ComportamientoSerie Comportamiento)
    : IRequest<ReglaLiberacionSerieAdminResponse>;

public sealed class CrearReglaLiberacionSerieValidator : AbstractValidator<CrearReglaLiberacionSerieCommand>
{
    public CrearReglaLiberacionSerieValidator()
    {
        RuleFor(c => c.Prefijo).NotEmpty()
            .MaximumLength(ReglaLiberacionSerie.PrefijoMaxLength)
            .Matches("^[0-9]+$").WithMessage("La serie debe tener solo dígitos.");
        RuleFor(c => c.Comportamiento).IsInEnum();
    }
}

public sealed class CrearReglaLiberacionSerieHandler
    : IRequestHandler<CrearReglaLiberacionSerieCommand, ReglaLiberacionSerieAdminResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public CrearReglaLiberacionSerieHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<ReglaLiberacionSerieAdminResponse> Handle(
        CrearReglaLiberacionSerieCommand command, CancellationToken cancellationToken)
    {
        var prefijo = command.Prefijo.Trim();
        if (await _db.ReglasLiberacionSerie.AnyAsync(r => r.Prefijo == prefijo, cancellationToken))
            throw new ConflictException("RLS_PREFIJO_DUPLICADO",
                $"La serie {prefijo} ya tiene una regla; cámbiala o actívala en lugar de crear otra.");

        var regla = new ReglaLiberacionSerie(Guid.CreateVersion7(), prefijo, command.Comportamiento);
        _db.ReglasLiberacionSerie.Add(regla);
        await _db.SaveChangesAsync(cancellationToken);
        return ReglaLiberacionSerieMapper.ToResponse(regla);
    }
}

// --------------------------------------------------- Cambiar comportamiento / activar / desactivar

public sealed record CambiarComportamientoSerieCommand(Guid Id, int VersionEsperada, ComportamientoSerie Comportamiento)
    : IRequest<ReglaLiberacionSerieAdminResponse>;

public sealed class CambiarComportamientoSerieValidator : AbstractValidator<CambiarComportamientoSerieCommand>
{
    public CambiarComportamientoSerieValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.Comportamiento).IsInEnum();
    }
}

public sealed class CambiarComportamientoSerieHandler
    : IRequestHandler<CambiarComportamientoSerieCommand, ReglaLiberacionSerieAdminResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public CambiarComportamientoSerieHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<ReglaLiberacionSerieAdminResponse> Handle(
        CambiarComportamientoSerieCommand command, CancellationToken cancellationToken)
    {
        var regla = await ReglaLiberacionSerieCarga.ObtenerAsync(_db, command.Id, command.VersionEsperada, cancellationToken);
        regla.CambiarComportamiento(command.Comportamiento);
        await _db.SaveChangesAsync(cancellationToken);
        return ReglaLiberacionSerieMapper.ToResponse(regla);
    }
}

public sealed record CambiarEstadoReglaSerieCommand(Guid Id, int VersionEsperada, bool Activo)
    : IRequest<ReglaLiberacionSerieAdminResponse>;

public sealed class CambiarEstadoReglaSerieHandler
    : IRequestHandler<CambiarEstadoReglaSerieCommand, ReglaLiberacionSerieAdminResponse>
{
    private readonly CuentasPorCobrarDbContext _db;
    public CambiarEstadoReglaSerieHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<ReglaLiberacionSerieAdminResponse> Handle(
        CambiarEstadoReglaSerieCommand command, CancellationToken cancellationToken)
    {
        var regla = await ReglaLiberacionSerieCarga.ObtenerAsync(_db, command.Id, command.VersionEsperada, cancellationToken);
        if (command.Activo) regla.Activar(); else regla.Desactivar();
        await _db.SaveChangesAsync(cancellationToken);
        return ReglaLiberacionSerieMapper.ToResponse(regla);
    }
}

internal static class ReglaLiberacionSerieCarga
{
    public static async Task<ReglaLiberacionSerie> ObtenerAsync(
        CuentasPorCobrarDbContext db, Guid id, int versionEsperada, CancellationToken ct)
    {
        var regla = await db.ReglasLiberacionSerie.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new EntityNotFoundException("RLS_NO_ENCONTRADA",
                $"No se encontró la regla de serie '{id}'.");
        if (regla.Version != versionEsperada)
            throw new ConcurrencyException(nameof(ReglaLiberacionSerie), regla.Id);
        return regla;
    }
}

// --------------------------------------------------- Historial (bitácora central)

public sealed record HistorialReglaSerieResponse(
    Guid Id,
    Guid ReglaId,
    string Prefijo,
    string Operacion,
    ComportamientoSerie? ComportamientoAntes,
    ComportamientoSerie? ComportamientoDespues,
    bool? ActivoAntes,
    bool? ActivoDespues,
    string ActorNombre,
    DateTimeOffset Fecha);

/// <summary>Cambios a las reglas de serie, del más reciente al más antiguo (tope 200).</summary>
public sealed record ObtenerHistorialReglasSerieQuery(Guid? ReglaId = null)
    : IRequest<IReadOnlyList<HistorialReglaSerieResponse>>;

public sealed class ObtenerHistorialReglasSerieHandler
    : IRequestHandler<ObtenerHistorialReglasSerieQuery, IReadOnlyList<HistorialReglaSerieResponse>>
{
    private const int Tope = 200;
    private readonly CuentasPorCobrarDbContext _db;
    public ObtenerHistorialReglasSerieHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<HistorialReglaSerieResponse>> Handle(
        ObtenerHistorialReglasSerieQuery query, CancellationToken cancellationToken)
    {
        // El catálogo es global (sin empresa): se filtra por entidad y por
        // los ids de las reglas que existen en este esquema.
        var prefijos = await _db.ReglasLiberacionSerie.AsNoTracking()
            .Where(r => query.ReglaId == null || r.Id == query.ReglaId)
            .ToDictionaryAsync(r => r.Id, r => r.Prefijo, cancellationToken);
        if (prefijos.Count == 0) return [];

        var ids = prefijos.Keys.ToList();
        var filas = await _db.Set<AuditLogEntry>().AsNoTracking()
            .Where(a => a.Entidad == nameof(ReglaLiberacionSerie)
                && a.EntidadId != null && ids.Contains(a.EntidadId.Value))
            .OrderByDescending(a => a.Timestamp)
            .Take(Tope)
            .ToListAsync(cancellationToken);

        return filas.Select(a =>
        {
            var c = CambiosRegla.Leer(a.Cambios);
            return new HistorialReglaSerieResponse(
                a.Id, a.EntidadId!.Value, prefijos[a.EntidadId.Value], a.Operacion,
                c.ComportamientoAntes, c.ComportamientoDespues, c.ActivoAntes, c.ActivoDespues,
                a.ActorNombre, a.Timestamp);
        }).ToList();
    }
}

/// <summary>
/// Lee el JSON del interceptor de auditoría: <c>{ snapshot: {...} }</c> en
/// altas y <c>{ diff: { Prop: { antes, despues } } }</c> en cambios.
/// </summary>
internal static class CambiosRegla
{
    public sealed record Valores(
        ComportamientoSerie? ComportamientoAntes, ComportamientoSerie? ComportamientoDespues,
        bool? ActivoAntes, bool? ActivoDespues);

    public static Valores Leer(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.TryGetProperty("snapshot", out var snap))
                return new(null, Comportamiento(snap, "Comportamiento"), null, Booleano(snap, "Activo"));
            if (raiz.TryGetProperty("diff", out var diff))
            {
                ComportamientoSerie? ca = null, cd = null;
                bool? aa = null, ad = null;
                if (diff.TryGetProperty(nameof(ReglaLiberacionSerie.Comportamiento), out var c))
                {
                    ca = Comportamiento(c, "antes");
                    cd = Comportamiento(c, "despues");
                }
                if (diff.TryGetProperty(nameof(ReglaLiberacionSerie.Activo), out var act))
                {
                    aa = Booleano(act, "antes");
                    ad = Booleano(act, "despues");
                }
                return new(ca, cd, aa, ad);
            }
        }
        catch (JsonException)
        {
            // Fila con formato inesperado: se muestra sin detalle.
        }
        return new(null, null, null, null);
    }

    private static ComportamientoSerie? Comportamiento(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt16(out var n)
            && Enum.IsDefined((ComportamientoSerie)n)
            ? (ComportamientoSerie)n
            : null;

    private static bool? Booleano(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
