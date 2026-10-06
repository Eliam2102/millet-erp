using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Periodos;

public sealed record PeriodoContableResponse(
    Guid Id, Guid EjercicioId, int Anio, int Numero, string Nombre, DateOnly FechaInicio, DateOnly FechaFin, EstadoPeriodo Estado,
    bool EsAjuste, string? AbiertoPor, DateTimeOffset? AbiertoEn, string? CerradoPor, DateTimeOffset? CerradoEn,
    string? ReabiertoPor, DateTimeOffset? ReabiertoEn, int Version);

/// <summary><c>Version</c> es la del ejercicio: el <c>If-Match</c> de la apertura en lote. Cada periodo trae la suya.</summary>
public sealed record EjercicioContableResponse(Guid Id, int Anio, int Version, IReadOnlyList<PeriodoContableResponse> Periodos);

public sealed record BitacoraPeriodoResponse(
    Guid Id, AccionPeriodo Accion, EstadoPeriodo EstadoAnterior, EstadoPeriodo EstadoNuevo, string? Motivo, Guid? UsuarioId,
    string UsuarioNombre, DateTimeOffset OcurridoEn, int VersionResultante);

/// <summary>
/// Piezas comunes de los casos de uso de periodos (F1-CON-03). Todas las transiciones corren bajo un advisory lock: así las
/// reglas que miran a los periodos vecinos (cierre secuencial, reapertura, periodo 13) no tienen carreras, y dos cierres con la
/// misma versión se serializan (el segundo ve la versión nueva y recibe 409).
/// </summary>
internal static class PoliticaPeriodos
{
    // ponytail: el candado global prioriza integridad; dividir por empresa/ejercicio si el volumen exige más concurrencia.
    public const long LockPeriodos = 0x0D03_0001;

    public static PeriodoContableResponse Response(PeriodoContable p) => new(p.Id, p.EjercicioId, p.Anio, p.Numero,
        PeriodoContable.Nombre(p.Numero), p.FechaInicio, p.FechaFin, p.Estado, p.EsAjuste, p.AbiertoPor, p.AbiertoEn,
        p.CerradoPor, p.CerradoEn, p.ReabiertoPor, p.ReabiertoEn, p.Version);

    public static async Task<EjercicioContableResponse> ResponseAsync(ContabilidadDbContext db, EjercicioContable e, CancellationToken ct)
    {
        var periodos = await db.Periodos.AsNoTracking().Where(p => p.EjercicioId == e.Id).OrderBy(p => p.Numero).ToListAsync(ct);
        return new(e.Id, e.Anio, e.Version, [.. periodos.Select(Response)]);
    }

    public static async Task<PeriodoContable> PeriodoAsync(ContabilidadDbContext db, Guid id, int versionEsperada, CancellationToken ct)
    {
        var p = await db.Periodos.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo contable '{id}'.");
        if (p.Version != versionEsperada) throw new ConcurrencyException(nameof(PeriodoContable), id);
        return p;
    }

    public static string Usuario(ICurrentUserContext usuario) => usuario.UserName ?? "system";
}

/// <summary>D7: motivo de 10 a 500 caracteres en cerrar y reabrir (el dominio lo vuelve a exigir).</summary>
internal static class ReglasMotivo
{
    public static IRuleBuilderOptions<T, string> MotivoObligatorio<T>(this IRuleBuilder<T, string> r) =>
        r.Must(m => m is not null && m.Trim().Length is >= PeriodoContable.MotivoMinimo and <= PeriodoContable.MotivoMaximo)
            .WithMessage($"Indique el motivo (entre {PeriodoContable.MotivoMinimo} y {PeriodoContable.MotivoMaximo} caracteres).");
}

// ─── Consultas ───────────────────────────────────────────────────────────────

public sealed record ListarEjerciciosQuery : IRequest<IReadOnlyList<EjercicioContableResponse>>;

public sealed class ListarEjerciciosHandler(ContabilidadDbContext db) : IRequestHandler<ListarEjerciciosQuery, IReadOnlyList<EjercicioContableResponse>>
{
    public async Task<IReadOnlyList<EjercicioContableResponse>> Handle(ListarEjerciciosQuery request, CancellationToken cancellationToken)
    {
        var ejercicios = await db.Ejercicios.AsNoTracking().OrderByDescending(e => e.Anio).ToListAsync(cancellationToken);
        var ids = ejercicios.Select(e => e.Id).ToList();
        var periodos = (await db.Periodos.AsNoTracking().Where(p => ids.Contains(p.EjercicioId)).ToListAsync(cancellationToken))
            .ToLookup(p => p.EjercicioId);
        return [.. ejercicios.Select(e => new EjercicioContableResponse(e.Id, e.Anio, e.Version,
            [.. periodos[e.Id].OrderBy(p => p.Numero).Select(PoliticaPeriodos.Response)]))];
    }
}

public sealed record ObtenerEjercicioQuery(Guid Id) : IRequest<EjercicioContableResponse>;

public sealed class ObtenerEjercicioHandler(ContabilidadDbContext db) : IRequestHandler<ObtenerEjercicioQuery, EjercicioContableResponse>
{
    public async Task<EjercicioContableResponse> Handle(ObtenerEjercicioQuery request, CancellationToken cancellationToken)
    {
        var e = await db.Ejercicios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_EJERCICIO_NO_ENCONTRADO", $"No existe el ejercicio contable '{request.Id}'.");
        return await PoliticaPeriodos.ResponseAsync(db, e, cancellationToken);
    }
}

/// <summary>Historial del periodo: quién, cuándo, por qué, en orden.</summary>
public sealed record ObtenerBitacoraPeriodoQuery(Guid PeriodoId) : IRequest<IReadOnlyList<BitacoraPeriodoResponse>>;

public sealed class ObtenerBitacoraPeriodoHandler(ContabilidadDbContext db)
    : IRequestHandler<ObtenerBitacoraPeriodoQuery, IReadOnlyList<BitacoraPeriodoResponse>>
{
    public async Task<IReadOnlyList<BitacoraPeriodoResponse>> Handle(ObtenerBitacoraPeriodoQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Periodos.AnyAsync(p => p.Id == request.PeriodoId, cancellationToken))
            throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo contable '{request.PeriodoId}'.");
        return await db.PeriodosBitacora.AsNoTracking().Where(b => b.PeriodoId == request.PeriodoId).OrderBy(b => b.VersionResultante)
            .Select(b => new BitacoraPeriodoResponse(b.Id, b.Accion, b.EstadoAnterior, b.EstadoNuevo, b.Motivo, b.UsuarioId,
                b.UsuarioNombre, b.OcurridoEn, b.VersionResultante))
            .ToListAsync(cancellationToken);
    }
}

// ─── Crear ejercicio ─────────────────────────────────────────────────────────

/// <summary>Crea el ejercicio con sus 13 periodos sin abrir (D2). 409 si el año ya existe.</summary>
public sealed record CrearEjercicioContableCommand(int Anio) : IRequest<EjercicioContableResponse>;

public sealed class CrearEjercicioContableValidator : AbstractValidator<CrearEjercicioContableCommand>
{
    public CrearEjercicioContableValidator() =>
        RuleFor(c => c.Anio).InclusiveBetween(EjercicioContable.AnioMinimo, EjercicioContable.AnioMaximo)
            .WithMessage($"El año del ejercicio debe estar entre {EjercicioContable.AnioMinimo} y {EjercicioContable.AnioMaximo}.");
}

public sealed class CrearEjercicioContableHandler(ContabilidadDbContext db) : IRequestHandler<CrearEjercicioContableCommand, EjercicioContableResponse>
{
    public async Task<EjercicioContableResponse> Handle(CrearEjercicioContableCommand request, CancellationToken cancellationToken)
    {
        var ejercicio = new EjercicioContable(Guid.CreateVersion7(), request.Anio);
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaPeriodos.LockPeriodos, async ct =>
        {
            if (await db.Ejercicios.AnyAsync(e => e.Anio == request.Anio, ct))
                throw new ConflictException("CONTAB_EJERCICIO_EXISTE", $"El ejercicio {request.Anio} ya existe.");
            db.Ejercicios.Add(ejercicio);
            db.Periodos.AddRange(ejercicio.GenerarPeriodos());
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await PoliticaPeriodos.ResponseAsync(db, ejercicio, cancellationToken);
    }
}

// ─── Abrir (en lote) ─────────────────────────────────────────────────────────

/// <summary>
/// Abre uno o varios periodos del ejercicio («abrir enero a diciembre»). Todo o nada: si uno no se puede abrir no se abre
/// ninguno. Usa la versión del ejercicio como candado del lote y la incrementa.
/// </summary>
public sealed record AbrirPeriodosCommand(Guid EjercicioId, int VersionEsperada, IReadOnlyList<int> Numeros, string? Motivo)
    : IRequest<EjercicioContableResponse>;

public sealed class AbrirPeriodosValidator : AbstractValidator<AbrirPeriodosCommand>
{
    public AbrirPeriodosValidator()
    {
        RuleFor(c => c.Numeros).NotEmpty().WithMessage("Indique los periodos a abrir.")
            .Must(n => n.Distinct().Count() == n.Count).WithMessage("Hay periodos repetidos.");
        RuleForEach(c => c.Numeros).InclusiveBetween(1, PeriodoContable.NumeroAjuste).WithMessage("El número de periodo debe estar entre 1 y 13.");
        RuleFor(c => c.Motivo).MaximumLength(PeriodoContable.MotivoMaximo);
    }
}

public sealed class AbrirPeriodosHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<AbrirPeriodosCommand, EjercicioContableResponse>
{
    public async Task<EjercicioContableResponse> Handle(AbrirPeriodosCommand request, CancellationToken cancellationToken)
    {
        EjercicioContable? ejercicio = null;
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaPeriodos.LockPeriodos, async ct =>
        {
            ejercicio = await db.Ejercicios.FirstOrDefaultAsync(e => e.Id == request.EjercicioId, ct)
                ?? throw new EntityNotFoundException("CONTAB_EJERCICIO_NO_ENCONTRADO", $"No existe el ejercicio contable '{request.EjercicioId}'.");
            if (ejercicio.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(EjercicioContable), ejercicio.Id);
            var periodos = await db.Periodos.Where(p => p.EjercicioId == ejercicio.Id).ToDictionaryAsync(p => p.Numero, ct);
            foreach (var n in request.Numeros.Order())
                db.PeriodosBitacora.Add(periodos[n].Abrir(periodos[12], request.Motivo, usuario.UserId, PoliticaPeriodos.Usuario(usuario), clock.UtcNow));
            db.Entry(ejercicio).State = EntityState.Modified; // nueva versión del ejercicio = nuevo ETag del lote
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await PoliticaPeriodos.ResponseAsync(db, ejercicio!, cancellationToken);
    }
}

// ─── Cerrar ──────────────────────────────────────────────────────────────────

/// <summary>
/// Cierra un periodo abierto (D4: los anteriores del ejercicio cerrados). Versión distinta → 409 <c>CONCURRENCY_CONFLICT</c>;
/// ya cerrado → 409 <c>CONTAB_PERIODO_YA_CERRADO</c>. No toca el inventario (D10).
/// </summary>
public sealed record CerrarPeriodoCommand(Guid PeriodoId, int VersionEsperada, string Motivo) : IRequest<PeriodoContableResponse>;

public sealed class CerrarPeriodoValidator : AbstractValidator<CerrarPeriodoCommand>
{
    public CerrarPeriodoValidator() => RuleFor(c => c.Motivo).MotivoObligatorio();
}

public sealed class CerrarPeriodoHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<CerrarPeriodoCommand, PeriodoContableResponse>
{
    public async Task<PeriodoContableResponse> Handle(CerrarPeriodoCommand request, CancellationToken cancellationToken)
    {
        PeriodoContable? periodo = null;
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaPeriodos.LockPeriodos, async ct =>
        {
            periodo = await PoliticaPeriodos.PeriodoAsync(db, request.PeriodoId, request.VersionEsperada, ct);
            var p = periodo;
            var anteriores = await db.Periodos.AsNoTracking().Where(x => x.EjercicioId == p.EjercicioId && x.Numero < p.Numero).ToListAsync(ct);
            db.PeriodosBitacora.Add(p.Cerrar(anteriores, request.Motivo, usuario.UserId, PoliticaPeriodos.Usuario(usuario), clock.UtcNow));
            // PLATFORM-TODO(<OutboxContabilidad>): PeriodoContableCerradoEvent cuando haya outbox y suscriptores (C1.2/C1.5).
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return PoliticaPeriodos.Response(periodo!);
    }
}

// ─── Reabrir ─────────────────────────────────────────────────────────────────

/// <summary>
/// Reabre un periodo cerrado (autorización de mayor nivel, permiso aparte). D4: el siguiente no debe estar cerrado.
/// Reabrir Contabilidad NO reabre <c>almacen.periodos_cerrados</c> (D10/D18).
/// </summary>
public sealed record ReabrirPeriodoCommand(Guid PeriodoId, int VersionEsperada, string Motivo) : IRequest<PeriodoContableResponse>;

public sealed class ReabrirPeriodoValidator : AbstractValidator<ReabrirPeriodoCommand>
{
    public ReabrirPeriodoValidator() => RuleFor(c => c.Motivo).MotivoObligatorio();
}

public sealed class ReabrirPeriodoHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<ReabrirPeriodoCommand, PeriodoContableResponse>
{
    public async Task<PeriodoContableResponse> Handle(ReabrirPeriodoCommand request, CancellationToken cancellationToken)
    {
        PeriodoContable? periodo = null;
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaPeriodos.LockPeriodos, async ct =>
        {
            periodo = await PoliticaPeriodos.PeriodoAsync(db, request.PeriodoId, request.VersionEsperada, ct);
            var p = periodo;
            var siguiente = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(x => x.EjercicioId == p.EjercicioId && x.Numero == p.Numero + 1, ct);
            db.PeriodosBitacora.Add(p.Reabrir(siguiente, request.Motivo, usuario.UserId, PoliticaPeriodos.Usuario(usuario), clock.UtcNow));
            // PLATFORM-TODO(<OutboxContabilidad>): PeriodoContableReabiertoEvent cuando haya outbox y suscriptores (C1.2/C1.5).
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return PoliticaPeriodos.Response(periodo!);
    }
}
