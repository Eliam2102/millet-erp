using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Periodos;

/// <summary>Permisos del recurso periodo (Contabilidad no referencia Identidad; mismos códigos que <c>PermisosCanonicos</c>).</summary>
public static class PermisosPeriodo
{
    public const string Leer = "contabilidad.periodo.leer";
    public const string Administrar = "contabilidad.periodo.administrar";
    public const string Cerrar = "contabilidad.periodo.cerrar";
    public const string Reabrir = "contabilidad.periodo.reabrir";
}

public sealed record PeriodoResponse(
    Guid Id, int Ejercicio, int Numero, string Etiqueta, bool EsPeriodoAjustes, EstadoPeriodo Estado,
    DateOnly? FechaInicio, DateOnly? FechaFin, string? CerradoPor, DateTimeOffset? CerradoEn,
    string? ReabiertoPor, DateTimeOffset? ReabiertoEn, int Version);

public sealed record PeriodoEventoResponse(Guid Id, AccionPeriodo Accion, string Usuario, DateTimeOffset Fecha, string? Motivo);

public sealed record EjercicioResponse(int Ejercicio, IReadOnlyList<PeriodoResponse> Periodos);

internal static class PeriodosMapeo
{
    public static PeriodoResponse Response(PeriodoContable p) => new(
        p.Id, p.Ejercicio, p.Numero, p.Etiqueta, p.EsPeriodoAjustes, p.Estado, p.FechaInicio, p.FechaFin,
        p.CerradoPor, p.CerradoEn, p.ReabiertoPor, p.ReabiertoEn, p.Version);

    public static string Usuario(ICurrentUserContext u) => u.UserName ?? u.Email ?? "sistema";

    public static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ─── Consultas ───────────────────────────────────────────────────────────────

/// <summary>Los 13 periodos de un ejercicio, en orden. Lista vacía si el ejercicio no se ha creado.</summary>
public sealed record ListarPeriodosQuery(int Ejercicio) : IRequest<IReadOnlyList<PeriodoResponse>>;

public sealed class ListarPeriodosHandler(ContabilidadDbContext db) : IRequestHandler<ListarPeriodosQuery, IReadOnlyList<PeriodoResponse>>
{
    public async Task<IReadOnlyList<PeriodoResponse>> Handle(ListarPeriodosQuery request, CancellationToken cancellationToken) =>
        (await db.Periodos.AsNoTracking().Where(p => p.Ejercicio == request.Ejercicio).OrderBy(p => p.Numero).ToListAsync(cancellationToken))
        .Select(PeriodosMapeo.Response).ToList();
}

/// <summary>Ejercicios creados, del más reciente al más antiguo.</summary>
public sealed record ListarEjerciciosQuery : IRequest<IReadOnlyList<int>>;

public sealed class ListarEjerciciosHandler(ContabilidadDbContext db) : IRequestHandler<ListarEjerciciosQuery, IReadOnlyList<int>>
{
    public async Task<IReadOnlyList<int>> Handle(ListarEjerciciosQuery request, CancellationToken cancellationToken) =>
        await db.Periodos.AsNoTracking().Select(p => p.Ejercicio).Distinct().OrderByDescending(e => e).ToListAsync(cancellationToken);
}

public sealed record ObtenerPeriodoQuery(Guid Id) : IRequest<PeriodoResponse>;

public sealed class ObtenerPeriodoHandler(ContabilidadDbContext db) : IRequestHandler<ObtenerPeriodoQuery, PeriodoResponse>
{
    public async Task<PeriodoResponse> Handle(ObtenerPeriodoQuery request, CancellationToken cancellationToken) =>
        PeriodosMapeo.Response(await db.Periodos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo '{request.Id}'."));
}

public sealed record HistorialPeriodoQuery(Guid Id) : IRequest<IReadOnlyList<PeriodoEventoResponse>>;

public sealed class HistorialPeriodoHandler(ContabilidadDbContext db) : IRequestHandler<HistorialPeriodoQuery, IReadOnlyList<PeriodoEventoResponse>>
{
    public async Task<IReadOnlyList<PeriodoEventoResponse>> Handle(HistorialPeriodoQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Periodos.AnyAsync(p => p.Id == request.Id, cancellationToken))
            throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo '{request.Id}'.");
        return await db.PeriodosEventos.AsNoTracking().Where(e => e.PeriodoId == request.Id)
            .OrderByDescending(e => e.Fecha).ThenByDescending(e => e.Id)
            .Select(e => new PeriodoEventoResponse(e.Id, e.Accion, e.Usuario, e.Fecha, e.Motivo))
            .ToListAsync(cancellationToken);
    }
}

// ─── Crear ejercicio ─────────────────────────────────────────────────────────

/// <summary>Crea los 13 periodos del ejercicio, abiertos, en una sola transacción. Si ya existe: 409.</summary>
public sealed record CrearEjercicioCommand(int Ejercicio) : IRequest<EjercicioResponse>;

public sealed class CrearEjercicioValidator : AbstractValidator<CrearEjercicioCommand>
{
    public CrearEjercicioValidator() => RuleFor(c => c.Ejercicio).InclusiveBetween(2000, 2100);
}

public sealed class CrearEjercicioHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<CrearEjercicioCommand, EjercicioResponse>
{
    public async Task<EjercicioResponse> Handle(CrearEjercicioCommand request, CancellationToken cancellationToken)
    {
        PeriodoContable.ValidarEjercicio(request.Ejercicio);
        if (await db.Periodos.AnyAsync(p => p.Ejercicio == request.Ejercicio, cancellationToken))
            throw new ConflictException("CONTAB_EJERCICIO_YA_EXISTE", $"El ejercicio {request.Ejercicio} ya tiene sus periodos.");

        var quien = PeriodosMapeo.Usuario(usuario);
        var ahora = clock.UtcNow;
        var periodos = Enumerable.Range(1, PeriodoContable.PeriodoAjustes)
            .Select(n => new PeriodoContable(Guid.CreateVersion7(), request.Ejercicio, n)).ToList();
        db.Periodos.AddRange(periodos);
        db.PeriodosEventos.AddRange(periodos.Select(p =>
            new PeriodoContableEvento(Guid.CreateVersion7(), p.Id, AccionPeriodo.Creado, quien, ahora, null)));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await OtroLoCreoAsync(request.Ejercicio, cancellationToken))
                throw new ConflictException("CONTAB_EJERCICIO_YA_EXISTE", $"El ejercicio {request.Ejercicio} ya tiene sus periodos.");
            throw;
        }
        return new EjercicioResponse(request.Ejercicio, periodos.Select(PeriodosMapeo.Response).ToList());
    }

    /// <summary>Dos altas simultáneas del mismo ejercicio: la segunda choca con el índice único y responde 409, no 500.</summary>
    private async Task<bool> OtroLoCreoAsync(int ejercicio, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.Periodos.AnyAsync(p => p.Ejercicio == ejercicio, ct);
    }
}

// ─── Cerrar y reabrir ────────────────────────────────────────────────────────

public sealed record CerrarPeriodoCommand(Guid Id, int VersionEsperada, string? Motivo) : IRequest<PeriodoResponse>;

public sealed class CerrarPeriodoValidator : AbstractValidator<CerrarPeriodoCommand>
{
    public CerrarPeriodoValidator() => RuleFor(c => c.Motivo).MaximumLength(PeriodoContable.MaxMotivo);
}

public sealed class CerrarPeriodoHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<CerrarPeriodoCommand, PeriodoResponse>
{
    public async Task<PeriodoResponse> Handle(CerrarPeriodoCommand request, CancellationToken cancellationToken)
    {
        var periodo = await db.Periodos.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo '{request.Id}'.");
        if (periodo.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(PeriodoContable), request.Id);
        var quien = PeriodosMapeo.Usuario(usuario);
        var ahora = clock.UtcNow;
        periodo.Cerrar(quien, ahora);
        db.PeriodosEventos.Add(new PeriodoContableEvento(Guid.CreateVersion7(), periodo.Id, AccionPeriodo.Cerrado, quien, ahora,
            PeriodosMapeo.Texto(request.Motivo)));
        await db.SaveChangesAsync(cancellationToken);
        return PeriodosMapeo.Response(periodo);
    }
}

/// <summary>
/// R18: reabrir lo autoriza y lo hace el Contador General (permiso <c>contabilidad.periodo.reabrir</c>), con motivo.
/// Los saldos se calculan desde las pólizas, así que reabrir no rehace acumulados: el arrastre ocurre al recalcular (C1.3/C1.7).
/// Reabrir contabilidad no toca el cierre de inventario de Almacén (D18).
/// </summary>
public sealed record ReabrirPeriodoCommand(Guid Id, int VersionEsperada, string Motivo) : IRequest<PeriodoResponse>;

public sealed class ReabrirPeriodoValidator : AbstractValidator<ReabrirPeriodoCommand>
{
    public ReabrirPeriodoValidator() => RuleFor(c => c.Motivo).NotEmpty().MaximumLength(PeriodoContable.MaxMotivo);
}

public sealed class ReabrirPeriodoHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<ReabrirPeriodoCommand, PeriodoResponse>
{
    public async Task<PeriodoResponse> Handle(ReabrirPeriodoCommand request, CancellationToken cancellationToken)
    {
        var periodo = await db.Periodos.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo '{request.Id}'.");
        if (periodo.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(PeriodoContable), request.Id);
        var quien = PeriodosMapeo.Usuario(usuario);
        var ahora = clock.UtcNow;
        var motivo = PeriodosMapeo.Texto(request.Motivo);
        periodo.Reabrir(quien, motivo, ahora);
        db.PeriodosEventos.Add(new PeriodoContableEvento(Guid.CreateVersion7(), periodo.Id, AccionPeriodo.Reabierto, quien, ahora, motivo));
        await db.SaveChangesAsync(cancellationToken);
        return PeriodosMapeo.Response(periodo);
    }
}
