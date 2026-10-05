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

/// <summary><c>Estado</c>: Futura (aún no inicia, editable), Vigente o Cerrada (con fecha final ya pasada).</summary>
public sealed record ReglaDimensionResponse(
    Guid Id, Guid CuentaId, string CuentaCodigo, string CuentaNombre, Guid? TipoDocumentoId, string? TipoDocumentoClave,
    string? TipoDocumentoNombre, DimensionContable Dimension, string NombreDimension, RequerimientoDimension Requerimiento,
    DateOnly VigenteDesde, DateOnly? VigenteHasta, string Estado, bool Editable, bool EsPrueba, string? Nota, int Version);

/// <summary>Reglas comunes de alta, edición y cierre (D4): sin traslapes, sin fechas retroactivas y solo reglas futuras se editan.</summary>
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

    public async Task<ReglaDimensionResponse> ResponseAsync(ReglaDimension r, CancellationToken ct) =>
        (await ResponsesAsync([r], ct))[0];

    public async Task<IReadOnlyList<ReglaDimensionResponse>> ResponsesAsync(IReadOnlyList<ReglaDimension> reglas, CancellationToken ct)
    {
        var cuentaIds = reglas.Select(r => r.CuentaId).Distinct().ToList();
        var tipoIds = reglas.Where(r => r.TipoDocumentoId is not null).Select(r => r.TipoDocumentoId!.Value).Distinct().ToList();
        var cuentas = await db.Cuentas.AsNoTracking().Where(c => cuentaIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var tipos = await db.TiposDocumento.AsNoTracking().Where(t => tipoIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var hoy = Hoy;
        return [.. reglas.Select(r =>
        {
            var c = cuentas[r.CuentaId];
            var t = r.TipoDocumentoId is { } tid ? tipos[tid] : null;
            var estado = r.VigenteDesde > hoy ? "Futura" : r.VigenteHasta is { } h && h < hoy ? "Cerrada" : "Vigente";
            return new ReglaDimensionResponse(r.Id, r.CuentaId, c.Codigo, c.Nombre, r.TipoDocumentoId, t?.Clave, t?.Nombre,
                r.Dimension, opciones.Nombre(r.Dimension), r.Requerimiento, r.VigenteDesde, r.VigenteHasta, estado,
                Editable: r.VigenteDesde > hoy, r.EsPrueba, r.Nota, r.Version);
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
        if (regla.VigenteDesde <= p.Hoy)
            throw new BusinessRuleException("CONTAB_REGLA_INICIADA_NO_EDITABLE",
                $"Esta regla está en vigor desde el {regla.VigenteDesde:dd/MM/yyyy}: para cambiar la política ciérrela y cree una nueva, así se conserva la historia.");
        p.ValidarNoRetroactiva(request.VigenteDesde, "La fecha de inicio");
        regla.Reprogramar(request.Requerimiento, request.VigenteDesde, request.VigenteHasta, CrearReglaHandler.Texto(request.Nota));
        await PostgresAdvisoryLock.ExecuteAsync(db, PoliticaReglas.LockReglas, async ct =>
        {
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
        regla.Cerrar(request.VigenteHasta);
        await db.SaveChangesAsync(cancellationToken);
        return await p.ResponseAsync(regla, cancellationToken);
    }
}
