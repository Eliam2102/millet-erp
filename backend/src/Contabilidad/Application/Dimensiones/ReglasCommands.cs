using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.Catalogos.Domain;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Dimensiones;

/// <summary>
/// <c>Estado</c>: Futura (aún no inicia), Vigente o Cerrada (fecha final ya pasada). <c>Usada</c> = ya validó al menos un movimiento;
/// <c>Editable</c> = se puede editar o borrar (no usada). <c>UltimaFechaUso</c> = la fecha contable más reciente que validó.
/// </summary>
public sealed record ReglaDimensionResponse(
    Guid Id, Guid CuentaId, string CuentaCodigo, string CuentaNombre, Guid? TipoDocumentoId, string? TipoDocumentoClave,
    string? TipoDocumentoNombre, DimensionContable Dimension, string NombreDimension, RequerimientoDimension Requerimiento,
    DateOnly VigenteDesde, DateOnly? VigenteHasta, string Estado, bool Editable, bool EsPrueba, string? Nota, int Version,
    bool Usada = false, DateOnly? UltimaFechaUso = null);

/// <summary>
/// Reglas comunes de alta, edición, cierre y borrado (D4): sin traslapes, sin fechas retroactivas y la historia se protege por USO,
/// no por fecha: una regla que ya validó un movimiento no se edita ni se borra (aunque sea futura, porque un movimiento con fecha
/// contable futura también se valida con ella); solo se cierra, y no antes de la última fecha contable que validó.
/// </summary>
internal sealed class PoliticaReglas(ContabilidadDbContext db, DimensionesOpciones opciones, IClock clock)
{
    /// <summary>Serializa las escrituras de reglas para que el chequeo de traslape no tenga carreras.</summary>
    public const long LockReglas = 0x0D02_0001;

    public DateOnly Hoy => opciones.Hoy(clock.UtcNow);

    public void ValidarNoRetroactiva(DateOnly fecha, string que)
    {
        if (!opciones.PermitirVigenciaRetroactiva && fecha < Hoy)
            throw new BusinessRuleException("CONTAB_REGLA_VIGENCIA_RETROACTIVA",
                $"{que} no puede ser anterior a hoy ({Hoy:dd/MM/yyyy}): una regla no cambia la validación de fechas pasadas.");
    }

    public async Task ValidarSinTraslapeAsync(ReglaDimension regla, CancellationToken ct)
    {
        var otras = await db.ReglasDimension.AsNoTracking()
            .Where(r => r.Id != regla.Id && r.CuentaId == regla.CuentaId && r.TipoDocumentoId == regla.TipoDocumentoId && r.Dimension == regla.Dimension)
            .ToListAsync(ct);
        var choque = otras.FirstOrDefault(o => o.SeTraslapaCon(regla.VigenteDesde, regla.VigenteHasta));
        if (choque is not null)
            throw new ConflictException("CONTAB_REGLA_VIGENCIA_TRASLAPADA",
                $"Ya hay una regla para esta cuenta, tipo de documento y dimensión vigente del {choque.VigenteDesde:dd/MM/yyyy} "
                + (choque.VigenteHasta is { } h ? $"al {h:dd/MM/yyyy}" : "en adelante")
                + ". Cierre esa regla antes de crear otra o elija otra fecha de inicio.");
    }

    /// <summary>Última fecha contable validada por cada regla (solo las usadas aparecen).</summary>
    public async Task<Dictionary<Guid, DateOnly>> UsosAsync(IReadOnlyCollection<Guid> reglaIds, CancellationToken ct) =>
        await db.ReglasDimensionUso.AsNoTracking().Where(u => reglaIds.Contains(u.ReglaId))
            .GroupBy(u => u.ReglaId).Select(g => new { g.Key, Max = g.Max(u => u.FechaContable) })
            .ToDictionaryAsync(x => x.Key, x => x.Max, ct);

    public async Task ValidarSinUsoAsync(ReglaDimension regla, string accion, CancellationToken ct)
    {
        if ((await UsosAsync([regla.Id], ct)).TryGetValue(regla.Id, out var ultima))
            throw new BusinessRuleException("CONTAB_REGLA_USADA",
                $"Esta regla ya validó movimientos (el más reciente con fecha contable {ultima:dd/MM/yyyy}): no se puede {accion} sin alterar la historia. "
                + "Para cambiar la política ciérrela y cree una regla nueva.");
    }

    public async Task<ReglaDimensionResponse> ResponseAsync(ReglaDimension r, CancellationToken ct) =>
        (await ResponsesAsync([r], ct))[0];

    public async Task<IReadOnlyList<ReglaDimensionResponse>> ResponsesAsync(IReadOnlyList<ReglaDimension> reglas, CancellationToken ct)
    {
        var cuentaIds = reglas.Select(r => r.CuentaId).Distinct().ToList();
        var tipoIds = reglas.Where(r => r.TipoDocumentoId is not null).Select(r => r.TipoDocumentoId!.Value).Distinct().ToList();
        var cuentas = await db.Cuentas.AsNoTracking().Where(c => cuentaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var tipos = await db.TiposDocumento.AsNoTracking().Where(t => tipoIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var usos = await UsosAsync([.. reglas.Select(r => r.Id)], ct);
        var hoy = Hoy;
        return [.. reglas.Select(r =>
        {
            var c = cuentas[r.CuentaId];
            var t = r.TipoDocumentoId is { } tid ? tipos[tid] : null;
            var estado = r.VigenteDesde > hoy ? "Futura" : r.VigenteHasta is { } h && h < hoy ? "Cerrada" : "Vigente";
            return new ReglaDimensionResponse(r.Id, r.CuentaId, c.Codigo, c.Nombre, r.TipoDocumentoId, t?.Clave, t?.Nombre,
                r.Dimension, opciones.Nombre(r.Dimension), r.Requerimiento, r.VigenteDesde, r.VigenteHasta, estado,
                Editable: !usos.ContainsKey(r.Id), r.EsPrueba, r.Nota, r.Version,
                Usada: usos.ContainsKey(r.Id), UltimaFechaUso: usos.TryGetValue(r.Id, out var u) ? u : null);
        })];
    }
}

// ─── Listar / obtener ────────────────────────────────────────────────────────

public sealed record ListarReglasQuery(
    Guid? CuentaId, Guid? TipoDocumentoId, DimensionContable? Dimension, DateOnly? VigentesA, bool? EsPrueba, int Offset, int Limit)
    : IRequest<PagedResponse<ReglaDimensionResponse>>;

public sealed class ListarReglasHandler(ContabilidadDbContext db, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<ListarReglasQuery, PagedResponse<ReglaDimensionResponse>>
{
    public async Task<PagedResponse<ReglaDimensionResponse>> Handle(ListarReglasQuery request, CancellationToken cancellationToken)
    {
        var q = db.ReglasDimension.AsNoTracking();
        if (request.CuentaId is { } c) q = q.Where(r => r.CuentaId == c);
        if (request.TipoDocumentoId is { } t) q = q.Where(r => r.TipoDocumentoId == t);
        if (request.Dimension is { } d) q = q.Where(r => r.Dimension == d);
        if (request.EsPrueba is { } p) q = q.Where(r => r.EsPrueba == p);
        if (request.VigentesA is { } f) q = q.Where(r => r.VigenteDesde <= f && (r.VigenteHasta == null || r.VigenteHasta >= f));
        var total = await q.CountAsync(cancellationToken);
        var codigos = db.Cuentas.AsNoTracking();
        var items = await q
            .Join(codigos, r => r.CuentaId, cu => cu.Id, (r, cu) => new { r, cu.Codigo })
            .OrderBy(x => x.Codigo).ThenBy(x => x.r.Dimension).ThenByDescending(x => x.r.VigenteDesde)
            .Skip(request.Offset).Take(request.Limit).Select(x => x.r).ToListAsync(cancellationToken);
        var res = await new PoliticaReglas(db, opciones.Value, clock).ResponsesAsync(items, cancellationToken);
        return new PagedResponse<ReglaDimensionResponse>(res, total, request.Offset, request.Limit);
    }
}

public sealed record ObtenerReglaQuery(Guid Id) : IRequest<ReglaDimensionResponse>;

public sealed class ObtenerReglaHandler(ContabilidadDbContext db, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<ObtenerReglaQuery, ReglaDimensionResponse>
{
    public async Task<ReglaDimensionResponse> Handle(ObtenerReglaQuery request, CancellationToken cancellationToken)
    {
        var r = await db.ReglasDimension.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_REGLA_NO_ENCONTRADA", $"No existe la regla '{request.Id}'.");
        return await new PoliticaReglas(db, opciones.Value, clock).ResponseAsync(r, cancellationToken);
    }
}

/// <summary>Matriz efectiva para una cuenta + tipo (null = solo reglas de todos los tipos) a una fecha (null = hoy).</summary>
public sealed record MatrizEfectivaQuery(Guid CuentaId, Guid? TipoDocumentoId, DateOnly? Fecha) : IRequest<IReadOnlyList<RequerimientoEfectivo>>;

public sealed class MatrizEfectivaHandler(ContabilidadDbContext db, ValidadorDimensiones validador, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<MatrizEfectivaQuery, IReadOnlyList<RequerimientoEfectivo>>
{
    public async Task<IReadOnlyList<RequerimientoEfectivo>> Handle(MatrizEfectivaQuery request, CancellationToken cancellationToken)
    {
        if (!await db.Cuentas.AnyAsync(c => c.Id == request.CuentaId, cancellationToken))
            throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.CuentaId}'.");
        return await validador.ResolverAsync(request.CuentaId, request.TipoDocumentoId,
            request.Fecha ?? opciones.Value.Hoy(clock.UtcNow), cancellationToken);
    }
}

// ─── Crear ───────────────────────────────────────────────────────────────────

public sealed record CrearReglaCommand(
    Guid CuentaId, Guid? TipoDocumentoId, DimensionContable Dimension, RequerimientoDimension Requerimiento,
    DateOnly VigenteDesde, DateOnly? VigenteHasta, bool EsPrueba, string? Nota) : IRequest<ReglaDimensionResponse>;

public sealed class CrearReglaValidator : AbstractValidator<CrearReglaCommand>
{
    public CrearReglaValidator()
    {
        RuleFor(c => c.CuentaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Dimension).IsInEnum();
        RuleFor(c => c.Requerimiento).IsInEnum();
        RuleFor(c => c.Nota).MaximumLength(500);
    }
}

public sealed class CrearReglaHandler(ContabilidadDbContext db, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<CrearReglaCommand, ReglaDimensionResponse>
{
    public async Task<ReglaDimensionResponse> Handle(CrearReglaCommand request, CancellationToken cancellationToken)
    {
        var p = new PoliticaReglas(db, opciones.Value, clock);
        var cuenta = await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CuentaId, cancellationToken)
            ?? throw new BusinessRuleException("CONTAB_REGLA_CUENTA_INVALIDA", "La cuenta contable indicada no existe.");
        if (cuenta.EsRubro)
            throw new BusinessRuleException("CONTAB_REGLA_CUENTA_INVALIDA", $"{cuenta.Codigo} es un rubro de reporte: las reglas se definen sobre cuentas o ramas del árbol.");
        if (!cuenta.Activa)
            throw new BusinessRuleException("CONTAB_REGLA_CUENTA_INVALIDA", $"La cuenta {cuenta.Codigo} está dada de baja.");
        if (request.TipoDocumentoId is { } tid && !await db.TiposDocumento.AnyAsync(t => t.Id == tid && t.Estatus == EstatusCatalogo.Activo, cancellationToken))
            throw new BusinessRuleException("CONTAB_REGLA_TIPO_DOC_INVALIDO", "El tipo de documento no existe o está inactivo.");
        p.ValidarNoRetroactiva(request.VigenteDesde, "La fecha de inicio");

        var regla = new ReglaDimension(Guid.CreateVersion7(), request.CuentaId, request.TipoDocumentoId, request.Dimension,
            request.Requerimiento, request.VigenteDesde, request.VigenteHasta, request.EsPrueba, Texto(request.Nota));
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaReglas.LockReglas, async ct =>
        {
            await p.ValidarSinTraslapeAsync(regla, ct);
            db.ReglasDimension.Add(regla);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await p.ResponseAsync(regla, cancellationToken);
    }

    internal static string? Texto(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

// ─── Editar (solo reglas futuras) ────────────────────────────────────────────

public sealed record EditarReglaCommand(
    Guid Id, int VersionEsperada, RequerimientoDimension Requerimiento, DateOnly VigenteDesde, DateOnly? VigenteHasta, string? Nota)
    : IRequest<ReglaDimensionResponse>;

public sealed class EditarReglaValidator : AbstractValidator<EditarReglaCommand>
{
    public EditarReglaValidator()
    {
        RuleFor(c => c.Requerimiento).IsInEnum();
        RuleFor(c => c.Nota).MaximumLength(500);
    }
}

public sealed class EditarReglaHandler(ContabilidadDbContext db, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<EditarReglaCommand, ReglaDimensionResponse>
{
    public async Task<ReglaDimensionResponse> Handle(EditarReglaCommand request, CancellationToken cancellationToken)
    {
        var p = new PoliticaReglas(db, opciones.Value, clock);
        var regla = await db.ReglasDimension.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_REGLA_NO_ENCONTRADA", $"No existe la regla '{request.Id}'.");
        if (regla.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(ReglaDimension), request.Id);
        // Sin usos se corrige libremente; solo una fecha de inicio NUEVA no puede quedar en el pasado.
        if (request.VigenteDesde != regla.VigenteDesde) p.ValidarNoRetroactiva(request.VigenteDesde, "La fecha de inicio");
        if (request.VigenteHasta is { } h && h != regla.VigenteHasta && regla.VigenteDesde <= p.Hoy) p.ValidarNoRetroactiva(h, "La fecha final");
        regla.Reprogramar(request.Requerimiento, request.VigenteDesde, request.VigenteHasta, CrearReglaHandler.Texto(request.Nota));
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaReglas.LockReglas, async ct =>
        {
            await p.ValidarSinUsoAsync(regla, "editar", ct);
            await p.ValidarSinTraslapeAsync(regla, ct);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await p.ResponseAsync(regla, cancellationToken);
    }
}

// ─── Cerrar vigencia ─────────────────────────────────────────────────────────

/// <summary>
/// Fija la fecha final. Sin retroactividad (configurable): una regla en vigor aplica al menos hasta hoy, así no
/// cambia la validación de movimientos ya registrados con ella (cada movimiento guarda además la regla con la que se validó).
/// </summary>
public sealed record CerrarReglaCommand(Guid Id, int VersionEsperada, DateOnly VigenteHasta) : IRequest<ReglaDimensionResponse>;

public sealed class CerrarReglaHandler(ContabilidadDbContext db, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<CerrarReglaCommand, ReglaDimensionResponse>
{
    public async Task<ReglaDimensionResponse> Handle(CerrarReglaCommand request, CancellationToken cancellationToken)
    {
        var p = new PoliticaReglas(db, opciones.Value, clock);
        var regla = await db.ReglasDimension.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_REGLA_NO_ENCONTRADA", $"No existe la regla '{request.Id}'.");
        if (regla.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(ReglaDimension), request.Id);
        if (regla.VigenteDesde <= p.Hoy) p.ValidarNoRetroactiva(request.VigenteHasta, "La fecha final de una regla en vigor");
        if ((await p.UsosAsync([regla.Id], cancellationToken)).TryGetValue(regla.Id, out var ultima) && request.VigenteHasta < ultima)
            throw new BusinessRuleException("CONTAB_REGLA_CIERRE_ANTES_DE_USO",
                $"La regla validó un movimiento con fecha contable {ultima:dd/MM/yyyy}: la fecha final no puede ser anterior a esa fecha.");
        regla.Cerrar(request.VigenteHasta);
        await db.SaveChangesAsync(cancellationToken);
        return await p.ResponseAsync(regla, cancellationToken);
    }
}

// ─── Eliminar (solo reglas sin usos) ─────────────────────────────────────────

/// <summary>Borra una regla capturada por error mientras no haya validado ningún movimiento. Con usos: cerrarla.</summary>
public sealed record EliminarReglaCommand(Guid Id, int VersionEsperada) : IRequest<Unit>;

public sealed class EliminarReglaHandler(ContabilidadDbContext db, IOptions<DimensionesOpciones> opciones, IClock clock)
    : IRequestHandler<EliminarReglaCommand, Unit>
{
    public async Task<Unit> Handle(EliminarReglaCommand request, CancellationToken cancellationToken)
    {
        var p = new PoliticaReglas(db, opciones.Value, clock);
        var regla = await db.ReglasDimension.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_REGLA_NO_ENCONTRADA", $"No existe la regla '{request.Id}'.");
        if (regla.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(ReglaDimension), request.Id);
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaReglas.LockReglas, async ct =>
        {
            await p.ValidarSinUsoAsync(regla, "borrar", ct);
            db.ReglasDimension.Remove(regla);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return Unit.Value;
    }
}
