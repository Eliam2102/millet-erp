using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.Contabilidad.Application.Periodos;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Cierre;

/// <summary>
/// <c>Contabilidad:Cierre</c>. <see cref="ExigirChecklist"/> = el cierre del periodo exige el checklist completo o con excepción
/// autorizada (CON-12). Las pruebas de integración de otros módulos que cierran periodos lo apagan.
/// </summary>
public sealed class CierreContableOpciones
{
    public const string Seccion = "Contabilidad:Cierre";
    public bool ExigirChecklist { get; set; } = true;
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum EstadoPasoChecklist { Cumple = 0, Pendiente = 1, NoAplica = 2, Excepcion = 3, Confirmado = 4 }

public sealed record MarcaPasoResponse(Guid Id, TipoMarcaPasoCierre Tipo, string Motivo, string UsuarioNombre, DateTimeOffset RegistradaEn);

/// <summary>Un paso evaluado. <c>Bloquea</c> = obligatorio y pendiente sin excepción: impide cerrar.</summary>
public sealed record PasoChecklistResponse(
    Guid PasoId, string Clave, string Nombre, string Descripcion, TipoPasoCierre Tipo, int Orden, bool Obligatorio,
    EstadoPasoChecklist Estado, string Resumen, IReadOnlyList<string> Detalle, string? Ruta, bool Bloquea, MarcaPasoResponse? Marca);

public sealed record CierreRegistradoResponse(Guid Id, string CerradoPor, DateTimeOffset CerradoEn, int Excepciones);

public sealed record ChecklistCierreResponse(
    Guid PeriodoId, string Periodo, string Nombre, EstadoPeriodo EstadoPeriodo, bool ExigeChecklist, bool PuedeCerrar, int Bloqueantes,
    int Excepciones, DateTimeOffset EvaluadoEn, IReadOnlyList<PasoChecklistResponse> Pasos, IReadOnlyList<CierreRegistradoResponse> Cierres);

public sealed record PasoCierreResponse(
    Guid Id, string Clave, string Nombre, string Descripcion, TipoPasoCierre Tipo, AlcancePasoCierre Alcance, int Orden, bool Obligatorio,
    bool Activo, string? Ruta, int Version);

/// <summary>
/// CON-12 (86e3jvvme) · evalúa el checklist de cierre de un periodo: corre el verificador de cada paso automático activo, aplica las
/// confirmaciones de los manuales y las excepciones autorizadas, y dice si se puede cerrar. Un verificador que falla no tumba el
/// checklist: su paso queda pendiente con el error (falla cerrada).
/// </summary>
public sealed class EvaluadorChecklistCierre(
    ContabilidadDbContext db, IEnumerable<IVerificadorPasoCierre> verificadores, IClock clock, IOptions<CierreContableOpciones> opciones,
    ILogger<EvaluadorChecklistCierre> log)
{
    public const long LockPasos = 0x0D0C_0012;

    private readonly Dictionary<string, IVerificadorPasoCierre> _verificadores =
        verificadores.GroupBy(v => v.Clave).ToDictionary(g => g.Key, g => g.Last());

    public bool Exige => opciones.Value.ExigirChecklist;

    public string? Ruta(string clave) => _verificadores.GetValueOrDefault(clave)?.Ruta;

    /// <summary>Da de alta los pasos con que nace el checklist (automáticos registrados y manuales P10.4) que aún no existen.</summary>
    public async Task AsegurarPasosAsync(CancellationToken ct)
    {
        var claves = await db.PasosCierre.AsNoTracking().Select(p => p.Clave).ToListAsync(ct);
        var faltan = _verificadores.Values.Where(v => !claves.Contains(v.Clave))
            .Select(v => new PasoCierreContable(Guid.CreateVersion7(), v.Clave, v.Nombre, v.Descripcion, TipoPasoCierre.Automatico, v.Alcance, v.Orden, true))
            .Concat(PasosCierre.Manuales.Where(m => !claves.Contains(m.Clave))
                .Select(m => new PasoCierreContable(Guid.CreateVersion7(), m.Clave, m.Nombre, m.Descripcion, TipoPasoCierre.Manual, AlcancePasoCierre.Mensual, m.Orden, true)))
            .ToList();
        if (faltan.Count == 0) return;
        await PostgresAdvisoryLock.ExecuteAsync(db, LockPasos, async c =>
        {
            var ya = await db.PasosCierre.AsNoTracking().Select(p => p.Clave).ToListAsync(c);
            db.PasosCierre.AddRange(faltan.Where(p => !ya.Contains(p.Clave)));
            await db.SaveChangesAsync(c);
        }, ct);
    }

    public async Task<ChecklistCierreResponse> EvaluarAsync(PeriodoContable periodo, CancellationToken ct)
    {
        await AsegurarPasosAsync(ct);
        var pasos = (await db.PasosCierre.AsNoTracking().Where(p => p.Activo).ToListAsync(ct))
            .Where(p => p.AplicaA(periodo.Numero)).OrderBy(p => p.Orden).ThenBy(p => p.Nombre).ToList();
        var marcas = await db.MarcasPasoCierre.AsNoTracking().Where(m => m.PeriodoId == periodo.Id && m.RetiradaEn == null).ToListAsync(ct);
        var contexto = new ContextoVerificacionCierre(periodo.Id, periodo.Anio, periodo.Numero, periodo.FechaInicio, periodo.FechaFin);

        var resultado = new List<PasoChecklistResponse>(pasos.Count);
        foreach (var paso in pasos)
        {
            var excepcion = marcas.Where(m => m.PasoClave == paso.Clave && m.Tipo == TipoMarcaPasoCierre.Excepcion).MaxBy(m => m.RegistradaEn);
            var confirmacion = marcas.Where(m => m.PasoClave == paso.Clave && m.Tipo == TipoMarcaPasoCierre.Confirmacion).MaxBy(m => m.RegistradaEn);
            var (verificacion, ruta) = await VerificarAsync(paso, confirmacion, contexto, ct);
            resultado.Add(Combinar(paso, verificacion, ruta, excepcion, confirmacion));
        }

        var cierres = await db.CierresPeriodo.AsNoTracking().Where(c => c.PeriodoId == periodo.Id).OrderByDescending(c => c.CerradoEn)
            .Select(c => new CierreRegistradoResponse(c.Id, c.CerradoPor, c.CerradoEn, c.Excepciones)).ToListAsync(ct);
        var bloqueantes = resultado.Count(p => p.Bloquea);
        return new ChecklistCierreResponse(periodo.Id, periodo.Clave, PeriodoContable.Nombre(periodo.Numero), periodo.Estado, Exige,
            periodo.Estado == EstadoPeriodo.Abierto && (!Exige || bloqueantes == 0), bloqueantes,
            resultado.Count(p => p.Estado == EstadoPasoChecklist.Excepcion), clock.UtcNow, resultado, cierres);
    }

    private async Task<(ResultadoVerificacionPaso Resultado, string? Ruta)> VerificarAsync(PasoCierreContable paso, MarcaPasoCierre? confirmacion,
        ContextoVerificacionCierre contexto, CancellationToken ct)
    {
        if (paso.Tipo == TipoPasoCierre.Manual)
            return (Manual(confirmacion), null);
        if (!_verificadores.TryGetValue(paso.Clave, out var verificador))
            return (ResultadoVerificacionPaso.Pendiente("El ERP ya no tiene cómo verificar este paso: desactívelo o autorice una excepción."), null);
        try
        {
            return (await verificador.VerificarAsync(contexto, ct), verificador.Ruta);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "CON-12: no se pudo verificar el paso {Paso} del periodo {Periodo}", paso.Clave, contexto.Clave);
            return (ResultadoVerificacionPaso.Pendiente($"No se pudo verificar: {e.Message}"), verificador.Ruta);
        }
    }

    /// <summary>
    /// Estado final del paso: lo verificado manda; un pendiente con excepción vigente queda «Excepción» y no bloquea; un manual
    /// confirmado queda «Confirmado». Solo bloquea un obligatorio pendiente sin excepción (un opcional pendiente solo se avisa).
    /// </summary>
    public static PasoChecklistResponse Combinar(PasoCierreContable paso, ResultadoVerificacionPaso verificacion, string? ruta,
        MarcaPasoCierre? excepcion, MarcaPasoCierre? confirmacion)
    {
        var (estado, marca) = verificacion.Estado switch
        {
            EstadoVerificacionPaso.Cumple when confirmacion is not null && paso.Tipo == TipoPasoCierre.Manual => (EstadoPasoChecklist.Confirmado, confirmacion),
            EstadoVerificacionPaso.Cumple => (EstadoPasoChecklist.Cumple, (MarcaPasoCierre?)null),
            EstadoVerificacionPaso.NoAplica => (EstadoPasoChecklist.NoAplica, null),
            _ when excepcion is not null => (EstadoPasoChecklist.Excepcion, excepcion),
            _ => (EstadoPasoChecklist.Pendiente, null),
        };
        return new PasoChecklistResponse(paso.Id, paso.Clave, paso.Nombre, paso.Descripcion, paso.Tipo, paso.Orden, paso.Obligatorio,
            estado, verificacion.Resumen, verificacion.Detalle, ruta, paso.Obligatorio && estado == EstadoPasoChecklist.Pendiente,
            marca is null ? null : Marca(marca));
    }

    /// <summary>Resultado de un paso manual: pendiente hasta que alguien lo confirma con nota.</summary>
    public static ResultadoVerificacionPaso Manual(MarcaPasoCierre? confirmacion) => confirmacion is null
        ? ResultadoVerificacionPaso.Pendiente("Paso manual: confírmelo con una nota cuando esté hecho.")
        : ResultadoVerificacionPaso.Cumple($"Confirmado por {confirmacion.UsuarioNombre}: {confirmacion.Motivo}");

    internal static MarcaPasoResponse Marca(MarcaPasoCierre m) => new(m.Id, m.Tipo, m.Motivo, m.UsuarioNombre, m.RegistradaEn);

    /// <summary>
    /// CA10.9: si el checklist exige y algún paso obligatorio sigue pendiente, el cierre no procede y el mensaje dice qué falta.
    /// Devuelve la evaluación para guardarla como foto del cierre (null si el checklist no se exige).
    /// </summary>
    public async Task<ChecklistCierreResponse?> ExigirAsync(PeriodoContable periodo, CancellationToken ct)
    {
        if (!Exige) return null;
        var checklist = await EvaluarAsync(periodo, ct);
        if (checklist.Bloqueantes == 0) return checklist;
        var faltan = checklist.Pasos.Where(p => p.Bloquea).Select(p => $"{p.Nombre} ({p.Resumen})");
        throw new BusinessRuleException("CONTAB_CIERRE_CHECKLIST_INCOMPLETO",
            $"No se puede cerrar {periodo.Clave}: falta {string.Join("; ", faltan)}. Resuélvalo o pida una excepción autorizada.");
    }

    public static CierrePeriodoRegistro Registro(PeriodoContable periodo, ChecklistCierreResponse checklist, string usuario, DateTimeOffset ahora) =>
        new(Guid.CreateVersion7(), periodo.Id, usuario, ahora, checklist.Excepciones, JsonSerializer.Serialize(checklist));
}

// ─── Consultas ───────────────────────────────────────────────────────────────

public sealed record ObtenerChecklistCierreQuery(Guid PeriodoId) : IRequest<ChecklistCierreResponse>;

public sealed class ObtenerChecklistCierreHandler(ContabilidadDbContext db, EvaluadorChecklistCierre evaluador)
    : IRequestHandler<ObtenerChecklistCierreQuery, ChecklistCierreResponse>
{
    public async Task<ChecklistCierreResponse> Handle(ObtenerChecklistCierreQuery request, CancellationToken cancellationToken)
    {
        var periodo = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PeriodoId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo contable '{request.PeriodoId}'.");
        return await evaluador.EvaluarAsync(periodo, cancellationToken);
    }
}

/// <summary>Foto del checklist guardada en un cierre (CON-12-a: con quién autorizó cada excepción).</summary>
public sealed record ObtenerCierreRegistradoQuery(Guid CierreId) : IRequest<ChecklistCierreResponse>;

public sealed class ObtenerCierreRegistradoHandler(ContabilidadDbContext db) : IRequestHandler<ObtenerCierreRegistradoQuery, ChecklistCierreResponse>
{
    public async Task<ChecklistCierreResponse> Handle(ObtenerCierreRegistradoQuery request, CancellationToken cancellationToken)
    {
        var r = await db.CierresPeriodo.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CierreId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CIERRE_NO_ENCONTRADO", $"No existe el cierre '{request.CierreId}'.");
        return JsonSerializer.Deserialize<ChecklistCierreResponse>(r.Checklist)!;
    }
}

public sealed record ListarPasosCierreQuery : IRequest<IReadOnlyList<PasoCierreResponse>>;

public sealed class ListarPasosCierreHandler(ContabilidadDbContext db, EvaluadorChecklistCierre evaluador)
    : IRequestHandler<ListarPasosCierreQuery, IReadOnlyList<PasoCierreResponse>>
{
    public async Task<IReadOnlyList<PasoCierreResponse>> Handle(ListarPasosCierreQuery request, CancellationToken cancellationToken)
    {
        await evaluador.AsegurarPasosAsync(cancellationToken);
        var pasos = await db.PasosCierre.AsNoTracking().ToListAsync(cancellationToken);
        return [.. pasos.OrderBy(p => p.Alcance).ThenBy(p => p.Orden).ThenBy(p => p.Nombre).Select(p => PoliticaPasos.Response(p, evaluador))];
    }
}

internal static class PoliticaPasos
{
    public static PasoCierreResponse Response(PasoCierreContable p, EvaluadorChecklistCierre evaluador) =>
        new(p.Id, p.Clave, p.Nombre, p.Descripcion, p.Tipo, p.Alcance, p.Orden, p.Obligatorio, p.Activo, evaluador.Ruta(p.Clave), p.Version);

    public static async Task<PeriodoContable> PeriodoNoCerradoAsync(ContabilidadDbContext db, Guid periodoId, CancellationToken ct)
    {
        var periodo = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodoId, ct)
            ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo contable '{periodoId}'.");
        if (periodo.Estado == EstadoPeriodo.Cerrado)
            throw new BusinessRuleException("CONTAB_CIERRE_PERIODO_CERRADO",
                $"El periodo {periodo.Clave} ya está cerrado: su checklist quedó en la foto del cierre. Reábralo para cambiarlo.");
        return periodo;
    }
}

// ─── Excepción y confirmación ────────────────────────────────────────────────

/// <summary>
/// Registra una excepción autorizada (CON-12-a: el permiso <c>contabilidad.cierre.autorizar-excepcion</c> lo revisa el endpoint) o
/// la confirmación de un paso manual. Una sola marca vigente por paso, tipo y periodo.
/// </summary>
public sealed record RegistrarMarcaPasoCierreCommand(Guid PeriodoId, string PasoClave, TipoMarcaPasoCierre Tipo, string Motivo)
    : IRequest<MarcaPasoResponse>;

public sealed class RegistrarMarcaPasoCierreValidator : AbstractValidator<RegistrarMarcaPasoCierreCommand>
{
    public RegistrarMarcaPasoCierreValidator()
    {
        RuleFor(c => c.PasoClave).NotEmpty().MaximumLength(PasoCierreContable.ClaveMaxima);
        RuleFor(c => c.Motivo).Must(m => m is not null && m.Trim().Length is >= MarcaPasoCierre.MotivoMinimo and <= MarcaPasoCierre.MotivoMaximo)
            .WithMessage($"Indique el motivo (entre {MarcaPasoCierre.MotivoMinimo} y {MarcaPasoCierre.MotivoMaximo} caracteres).");
    }
}

public sealed class RegistrarMarcaPasoCierreHandler(ContabilidadDbContext db, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<RegistrarMarcaPasoCierreCommand, MarcaPasoResponse>
{
    public async Task<MarcaPasoResponse> Handle(RegistrarMarcaPasoCierreCommand request, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var periodo = await PoliticaPasos.PeriodoNoCerradoAsync(db, request.PeriodoId, ct);
        var clave = request.PasoClave.Trim().ToUpperInvariant();
        var paso = await db.PasosCierre.AsNoTracking().FirstOrDefaultAsync(p => p.Clave == clave && p.Activo, ct)
            ?? throw new EntityNotFoundException("CONTAB_CIERRE_PASO_NO_ENCONTRADO", $"No hay un paso activo '{clave}' en el checklist de cierre.");
        if (!paso.AplicaA(periodo.Numero))
            throw new BusinessRuleException("CONTAB_CIERRE_PASO_NO_APLICA", $"El paso «{paso.Nombre}» no aplica al periodo {periodo.Clave}.");
        if (request.Tipo == TipoMarcaPasoCierre.Confirmacion && paso.Tipo != TipoPasoCierre.Manual)
            throw new BusinessRuleException("CONTAB_CIERRE_PASO_AUTOMATICO",
                $"«{paso.Nombre}» lo verifica el ERP: no se confirma a mano. Si no se cumple, se cierra solo con una excepción autorizada.");
        MarcaPasoCierre? marca = null;
        await PostgresAdvisoryLock.ExecuteAsync(db, EvaluadorChecklistCierre.LockPasos, async c =>
        {
            if (await db.MarcasPasoCierre.AnyAsync(m => m.PeriodoId == periodo.Id && m.PasoClave == clave && m.Tipo == request.Tipo && m.RetiradaEn == null, c))
                throw new ConflictException("CONTAB_CIERRE_MARCA_EXISTE",
                    $"El paso «{paso.Nombre}» ya tiene una {(request.Tipo == TipoMarcaPasoCierre.Excepcion ? "excepción" : "confirmación")} vigente en {periodo.Clave}.");
            marca = new MarcaPasoCierre(Guid.CreateVersion7(), periodo.Id, clave, request.Tipo, request.Motivo, usuario.UserId,
                PoliticaPeriodos.Usuario(usuario), clock.UtcNow);
            db.MarcasPasoCierre.Add(marca);
            await db.SaveChangesAsync(c);
        }, ct);
        return EvaluadorChecklistCierre.Marca(marca!);
    }
}

/// <summary>Retira una marca vigente. Retirar una excepción exige el mismo permiso que autorizarla.</summary>
public sealed record RetirarMarcaPasoCierreCommand(Guid MarcaId) : IRequest;

public sealed class RetirarMarcaPasoCierreHandler(ContabilidadDbContext db, ICurrentUserContext usuario, ICurrentUserPermissions permisos, IClock clock)
    : IRequestHandler<RetirarMarcaPasoCierreCommand>
{
    public const string PermisoExcepcion = "contabilidad.cierre.autorizar-excepcion";

    public async Task Handle(RetirarMarcaPasoCierreCommand request, CancellationToken cancellationToken)
    {
        var marca = await db.MarcasPasoCierre.FirstOrDefaultAsync(m => m.Id == request.MarcaId, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CIERRE_MARCA_NO_ENCONTRADA", $"No existe la marca '{request.MarcaId}'.");
        await PoliticaPasos.PeriodoNoCerradoAsync(db, marca.PeriodoId, cancellationToken);
        if (marca.Tipo == TipoMarcaPasoCierre.Excepcion && !await permisos.TieneAsync(PermisoExcepcion, cancellationToken))
            throw new ForbiddenException("CONTAB_CIERRE_SIN_PERMISO_EXCEPCION", "Retirar una excepción exige el permiso para autorizarlas.");
        marca.Retirar(PoliticaPeriodos.Usuario(usuario), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }
}

// ─── Configuración del checklist ─────────────────────────────────────────────

public sealed record ActualizarPasoCierreCommand(Guid Id, int VersionEsperada, string Nombre, string? Descripcion, int Orden, bool Obligatorio, bool Activo)
    : IRequest<PasoCierreResponse>;

public sealed class ActualizarPasoCierreValidator : AbstractValidator<ActualizarPasoCierreCommand>
{
    public ActualizarPasoCierreValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(PasoCierreContable.NombreMaximo);
        RuleFor(c => c.Descripcion).MaximumLength(PasoCierreContable.DescripcionMaxima);
        RuleFor(c => c.Orden).InclusiveBetween(0, 10_000);
    }
}

public sealed class ActualizarPasoCierreHandler(ContabilidadDbContext db, EvaluadorChecklistCierre evaluador)
    : IRequestHandler<ActualizarPasoCierreCommand, PasoCierreResponse>
{
    public async Task<PasoCierreResponse> Handle(ActualizarPasoCierreCommand request, CancellationToken cancellationToken)
    {
        var paso = await db.PasosCierre.FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CIERRE_PASO_NO_ENCONTRADO", $"No existe el paso '{request.Id}'.");
        if (paso.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(PasoCierreContable), paso.Id);
        paso.Configurar(request.Nombre, request.Descripcion, request.Orden, request.Obligatorio, request.Activo);
        await db.SaveChangesAsync(cancellationToken);
        return PoliticaPasos.Response(paso, evaluador);
    }
}

/// <summary>Paso manual propio de Contabilidad (lo que su checklist pida y el ERP no pueda verificar).</summary>
public sealed record CrearPasoManualCierreCommand(string Nombre, string? Descripcion, AlcancePasoCierre Alcance, int Orden, bool Obligatorio)
    : IRequest<PasoCierreResponse>;

public sealed class CrearPasoManualCierreValidator : AbstractValidator<CrearPasoManualCierreCommand>
{
    public CrearPasoManualCierreValidator()
    {
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(PasoCierreContable.NombreMaximo);
        RuleFor(c => c.Descripcion).MaximumLength(PasoCierreContable.DescripcionMaxima);
        RuleFor(c => c.Orden).InclusiveBetween(0, 10_000);
        RuleFor(c => c.Alcance).IsInEnum();
    }
}

public sealed class CrearPasoManualCierreHandler(ContabilidadDbContext db, EvaluadorChecklistCierre evaluador)
    : IRequestHandler<CrearPasoManualCierreCommand, PasoCierreResponse>
{
    public async Task<PasoCierreResponse> Handle(CrearPasoManualCierreCommand request, CancellationToken cancellationToken)
    {
        var paso = new PasoCierreContable(Guid.CreateVersion7(), $"MANUAL_{Guid.CreateVersion7():N}",
            request.Nombre, request.Descripcion ?? string.Empty, TipoPasoCierre.Manual, request.Alcance, request.Orden, request.Obligatorio);
        db.PasosCierre.Add(paso);
        await db.SaveChangesAsync(cancellationToken);
        return PoliticaPasos.Response(paso, evaluador);
    }
}
