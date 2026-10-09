using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Catalogos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Catalogos.Retenciones;

public sealed record RetencionResponse(Guid Id, string Concepto, string Descripcion, string Impuesto, decimal Tasa,
    string Fuente, bool Activa, string Aviso, string MotivoCambio, int Version);
public sealed record ListarRetencionesQuery : IRequest<IReadOnlyList<RetencionResponse>>;
public sealed class ListarRetencionesHandler(CuentasPorPagarDbContext db) : IRequestHandler<ListarRetencionesQuery, IReadOnlyList<RetencionResponse>>
{
    public async Task<IReadOnlyList<RetencionResponse>> Handle(ListarRetencionesQuery r, CancellationToken cancellationToken) =>
        (await db.RetencionesConcepto.AsNoTracking().OrderBy(r => r.Concepto).ThenBy(r => r.Impuesto).ToListAsync(cancellationToken)).Select(Mapear).ToArray();
    internal static RetencionResponse Mapear(RetencionConcepto r) => new(r.Id, r.Concepto, r.Descripcion, r.Impuesto,
        r.Tasa, r.Fuente, r.Activa, RetencionConcepto.AvisoFiscal, r.MotivoCambio, r.Version);
}
public sealed record GuardarRetencionCommand(Guid? Id, int? VersionEsperada, string Concepto, string Descripcion,
    string Impuesto, decimal Tasa, string Fuente, bool Activa, string Motivo) : IRequest<RetencionResponse>;
public sealed class GuardarRetencionValidator : AbstractValidator<GuardarRetencionCommand>
{
    public GuardarRetencionValidator()
    {
        RuleFor(r => r.Concepto).NotEmpty().MaximumLength(80); RuleFor(r => r.Descripcion).NotEmpty().MaximumLength(300);
        RuleFor(r => r.Impuesto).Must(i => i is "001" or "002" or "003").WithMessage("Elige un impuesto SAT: ISR, IVA o IEPS.");
        RuleFor(r => r.Tasa).InclusiveBetween(0, 1); RuleFor(r => r.Fuente).NotEmpty().MaximumLength(1000);
        RuleFor(r => r.Motivo).NotEmpty().Must(m => m is not null && m.Trim().Length is >= 10 and <= 500)
            .WithMessage("Explica el cambio en 10–500 caracteres.");
        RuleFor(r => r.VersionEsperada).NotNull().GreaterThanOrEqualTo(0).When(r => r.Id is not null);
    }
}
public sealed class GuardarRetencionHandler(CuentasPorPagarDbContext db) : IRequestHandler<GuardarRetencionCommand, RetencionResponse>
{
    public async Task<RetencionResponse> Handle(GuardarRetencionCommand c, CancellationToken cancellationToken)
    {
        RetencionConcepto r;
        if (c.Id is Guid id)
        {
            r = await db.RetencionesConcepto.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException("CXP_RETENCION_NO_ENCONTRADA", "No se encontró la retención.");
            if (r.Version != c.VersionEsperada) throw new ConcurrencyException(nameof(RetencionConcepto), id);
        }
        else { r = RetencionConcepto.Crear(c.Concepto, c.Descripcion, c.Impuesto, c.Tasa, c.Fuente, c.Motivo); db.RetencionesConcepto.Add(r); }
        var concepto = c.Concepto.Trim().ToUpperInvariant();
        if (await db.RetencionesConcepto.AnyAsync(x => x.Id != r.Id && x.Concepto == concepto && x.Impuesto == c.Impuesto && x.Tasa == c.Tasa, cancellationToken))
            throw new BusinessRuleException("CXP_RETENCION_DUPLICADA", "Ya existe la retención para ese concepto, impuesto y tasa.");
        r.Actualizar(c.Concepto, c.Descripcion, c.Impuesto, c.Tasa, c.Fuente, c.Activa, c.Motivo);
        await db.SaveChangesAsync(cancellationToken); return ListarRetencionesHandler.Mapear(r);
    }
}
public sealed record ProponerRetencionesQuery(string Concepto, decimal BaseNeta) : IRequest<IReadOnlyList<RetencionCfdi>>;
public sealed class ProponerRetencionesValidator : AbstractValidator<ProponerRetencionesQuery>
{
    public ProponerRetencionesValidator() { RuleFor(r => r.Concepto).NotEmpty().MaximumLength(80); RuleFor(r => r.BaseNeta).GreaterThanOrEqualTo(0); }
}
public sealed class ProponerRetencionesHandler(CuentasPorPagarDbContext db) : IRequestHandler<ProponerRetencionesQuery, IReadOnlyList<RetencionCfdi>>
{
    public async Task<IReadOnlyList<RetencionCfdi>> Handle(ProponerRetencionesQuery q, CancellationToken cancellationToken)
    {
        var concepto = q.Concepto.Trim().ToUpperInvariant();
        return ComparadorRetenciones.Proponer(q.BaseNeta, await db.RetencionesConcepto.AsNoTracking().Where(r => r.Concepto == concepto && r.Activa).ToListAsync(cancellationToken));
    }
}
