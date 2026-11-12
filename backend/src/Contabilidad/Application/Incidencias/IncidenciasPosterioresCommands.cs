using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Application.Polizas;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Incidencias;

public sealed record IncidenciaPosteriorResponse(Guid Id, Guid? PolizaId, string? PolizaFolio, int PeriodoAnio, int PeriodoNumero, string Periodo,
    string Hallazgo, string Impacto, decimal? ImporteImpacto, EstadoIncidenciaPosterior Estado, string RegistradaPor, DateTimeOffset RegistradaEn,
    ResolucionIncidencia? Resolucion, Guid? PolizaAjusteId, string? PolizaAjusteFolio, Guid? ReaperturaId, string? NotaResolucion, string? ResueltaPor,
    DateTimeOffset? ResueltaEn);

internal static class LecturaIncidencias
{
    public static async Task<List<IncidenciaPosteriorResponse>> ResponsesAsync(ContabilidadDbContext db, IReadOnlyList<IncidenciaPosterior> items, CancellationToken ct)
    {
        var ids = items.SelectMany(i => new[] { i.PolizaId, i.PolizaAjusteId }).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        var folios = await db.Polizas.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Folio }).ToDictionaryAsync(p => p.Id, p => p.Folio, ct);
        string? Folio(Guid? id) => id is { } g ? folios.GetValueOrDefault(g) : null;
        return [.. items.Select(i => new IncidenciaPosteriorResponse(i.Id, i.PolizaId, Folio(i.PolizaId), i.PeriodoAnio, i.PeriodoNumero,
            PeriodoContable.Etiqueta(i.PeriodoAnio, i.PeriodoNumero), i.Hallazgo, i.Impacto, i.ImporteImpacto, i.Estado, i.RegistradaPor, i.RegistradaEn,
            i.Resolucion, i.PolizaAjusteId, Folio(i.PolizaAjusteId), i.ReaperturaId, i.NotaResolucion, i.ResueltaPor, i.ResueltaEn))];
    }

    public static async Task<IncidenciaPosterior> CargarAsync(ContabilidadDbContext db, Guid id, CancellationToken ct) =>
        await db.IncidenciasPosteriores.FirstOrDefaultAsync(i => i.Id == id, ct)
        ?? throw new EntityNotFoundException("CONTAB_INCIDENCIA_NO_ENCONTRADA", $"No existe la incidencia '{id}'.");
}

// ---------------------------------------------------------------- Registrar

/// <summary>Liga a una póliza autorizada (el periodo sale de la póliza) o, sin póliza, a un periodo ya cerrado (<paramref name="Anio"/>, <paramref name="Numero"/>).</summary>
public sealed record RegistrarIncidenciaPosteriorCommand(Guid? PolizaId, int? Anio, int? Numero, string Hallazgo, string Impacto, decimal? ImporteImpacto)
    : IRequest<IncidenciaPosteriorResponse>;

public sealed class RegistrarIncidenciaPosteriorValidator : AbstractValidator<RegistrarIncidenciaPosteriorCommand>
{
    public RegistrarIncidenciaPosteriorValidator()
    {
        RuleFor(c => c).Must(c => c.PolizaId is not null || (c.Anio is not null && c.Numero is not null))
            .WithMessage("Indique la póliza autorizada o el periodo cerrado al que se liga la incidencia.");
        RuleFor(c => c.Hallazgo).Must(h => h is not null && h.Trim().Length is >= IncidenciaPosterior.HallazgoMinimo and <= IncidenciaPosterior.HallazgoMaximo)
            .WithMessage($"Describa qué se encontró (entre {IncidenciaPosterior.HallazgoMinimo} y {IncidenciaPosterior.HallazgoMaximo} caracteres).");
        RuleFor(c => c.Impacto).Must(h => h is not null && h.Trim().Length is >= IncidenciaPosterior.ImpactoMinimo and <= IncidenciaPosterior.ImpactoMaximo)
            .WithMessage($"Describa el impacto (entre {IncidenciaPosterior.ImpactoMinimo} y {IncidenciaPosterior.ImpactoMaximo} caracteres).");
    }
}

public sealed class RegistrarIncidenciaPosteriorHandler(ContabilidadDbContext db, AlcanceSucursalContable alcance, ICurrentUserContext usuario,
    ICurrentEmpresaContext empresa, IClock clock) : IRequestHandler<RegistrarIncidenciaPosteriorCommand, IncidenciaPosteriorResponse>
{
    public async Task<IncidenciaPosteriorResponse> Handle(RegistrarIncidenciaPosteriorCommand request, CancellationToken cancellationToken)
    {
        var empresaId = empresa.Current ?? throw new MissingEmpresaContextException(nameof(RegistrarIncidenciaPosteriorCommand));
        int anio, numero;
        if (request.PolizaId is { } polizaId)
        {
            var poliza = await db.Polizas.AsNoTracking().Include(p => p.Partidas).FirstOrDefaultAsync(p => p.Id == polizaId, cancellationToken)
                ?? throw new EntityNotFoundException("CONTAB_POLIZA_NO_ENCONTRADA", $"No existe la póliza '{polizaId}'.");
            foreach (var s in poliza.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
            if (poliza.Estado != EstadoPoliza.Autorizada)
                throw new BusinessRuleException("CONTAB_INCIDENCIA_POLIZA_NO_AUTORIZADA", "La incidencia se liga a una póliza autorizada.");
            (anio, numero) = (poliza.PeriodoAnio, poliza.PeriodoNumero);
        }
        else
        {
            (anio, numero) = (request.Anio!.Value, request.Numero!.Value);
            var periodo = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(p => p.Anio == anio && p.Numero == numero, cancellationToken)
                ?? throw new EntityNotFoundException("CONTAB_PERIODO_NO_ENCONTRADO", $"No existe el periodo {PeriodoContable.Etiqueta(anio, numero)}.");
            if (periodo.Estado != EstadoPeriodo.Cerrado)
                throw new BusinessRuleException("CONTAB_INCIDENCIA_PERIODO_NO_CERRADO",
                    $"El periodo {PeriodoContable.Etiqueta(anio, numero)} no está cerrado: la incidencia se liga a una póliza autorizada o a un periodo cerrado.");
        }
        var incidencia = new IncidenciaPosterior(Guid.CreateVersion7(), request.PolizaId, anio, numero, request.Hallazgo, request.Impacto, request.ImporteImpacto, usuario.UserId,
            PoliticaPolizas.Usuario(usuario), clock.UtcNow) { EmpresaId = empresaId };
        db.IncidenciasPosteriores.Add(incidencia);
        await db.SaveChangesAsync(cancellationToken);
        return (await LecturaIncidencias.ResponsesAsync(db, [incidencia], cancellationToken))[0];
    }
}

// ---------------------------------------------------------------- Resolver

/// <summary>Resuelve ligando la corrección: la póliza de ajuste o la reapertura acotada del periodo (exactamente una).</summary>
public sealed record ResolverIncidenciaPosteriorCommand(Guid Id, Guid? PolizaAjusteId, Guid? ReaperturaId, string? Nota) : IRequest<IncidenciaPosteriorResponse>;

public sealed class ResolverIncidenciaPosteriorValidator : AbstractValidator<ResolverIncidenciaPosteriorCommand>
{
    public ResolverIncidenciaPosteriorValidator() =>
        RuleFor(c => c).Must(c => (c.PolizaAjusteId is null) != (c.ReaperturaId is null))
            .WithMessage("Ligue la póliza de ajuste o la reapertura acotada (una de las dos).");
}

public sealed class ResolverIncidenciaPosteriorHandler(ContabilidadDbContext db, AlcanceSucursalContable alcance, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<ResolverIncidenciaPosteriorCommand, IncidenciaPosteriorResponse>
{
    public async Task<IncidenciaPosteriorResponse> Handle(ResolverIncidenciaPosteriorCommand request, CancellationToken cancellationToken)
    {
        var incidencia = await LecturaIncidencias.CargarAsync(db, request.Id, cancellationToken);
        if (!incidencia.Abierta) throw new ConflictException("CONTAB_INCIDENCIA_YA_RESUELTA", "La incidencia ya está resuelta.");
        if (request.PolizaAjusteId is { } ajusteId)
        {
            var ajuste = await db.Polizas.AsNoTracking().Include(p => p.Partidas).FirstOrDefaultAsync(p => p.Id == ajusteId, cancellationToken)
                ?? throw new EntityNotFoundException("CONTAB_POLIZA_NO_ENCONTRADA", $"No existe la póliza '{ajusteId}'.");
            foreach (var s in ajuste.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
            if (ajuste.Estado != EstadoPoliza.Autorizada)
                throw new BusinessRuleException("CONTAB_INCIDENCIA_AJUSTE_NO_AUTORIZADO", "La póliza de ajuste debe estar autorizada.");
            incidencia.ResolverConPolizaAjuste(ajusteId, request.Nota, usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow);
        }
        else
        {
            var reaperturaId = request.ReaperturaId!.Value;
            var reapertura = await db.Reaperturas.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reaperturaId, cancellationToken)
                ?? throw new EntityNotFoundException("CONTAB_REAPERTURA_NO_ENCONTRADA", $"No existe la reapertura '{reaperturaId}'.");
            if (reapertura.Anio != incidencia.PeriodoAnio || reapertura.Numero != incidencia.PeriodoNumero)
                throw new BusinessRuleException("CONTAB_INCIDENCIA_REAPERTURA_OTRO_PERIODO",
                    $"La reapertura es de otro periodo: la incidencia es de {PeriodoContable.Etiqueta(incidencia.PeriodoAnio, incidencia.PeriodoNumero)}.");
            incidencia.ResolverConReapertura(reaperturaId, request.Nota, usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow);
        }
        await db.SaveChangesAsync(cancellationToken);
        return (await LecturaIncidencias.ResponsesAsync(db, [incidencia], cancellationToken))[0];
    }
}

// ---------------------------------------------------------------- Consultar

public sealed record ListarIncidenciasPosterioresQuery(EstadoIncidenciaPosterior? Estado, int? Anio, int? Numero, Guid? PolizaId)
    : IRequest<IReadOnlyList<IncidenciaPosteriorResponse>>;

public sealed class ListarIncidenciasPosterioresHandler(ContabilidadDbContext db)
    : IRequestHandler<ListarIncidenciasPosterioresQuery, IReadOnlyList<IncidenciaPosteriorResponse>>
{
    public async Task<IReadOnlyList<IncidenciaPosteriorResponse>> Handle(ListarIncidenciasPosterioresQuery request, CancellationToken cancellationToken)
    {
        var consulta = db.IncidenciasPosteriores.AsNoTracking().AsQueryable();
        if (request.Estado is { } e) consulta = consulta.Where(i => i.Estado == e);
        if (request.Anio is { } a) consulta = consulta.Where(i => i.PeriodoAnio == a);
        if (request.Numero is { } n) consulta = consulta.Where(i => i.PeriodoNumero == n);
        if (request.PolizaId is { } p) consulta = consulta.Where(i => i.PolizaId == p);
        var items = await consulta.OrderByDescending(i => i.RegistradaEn).Take(500).ToListAsync(cancellationToken);
        return await LecturaIncidencias.ResponsesAsync(db, items, cancellationToken);
    }
}
