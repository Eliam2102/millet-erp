using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Polizas;

public sealed record PolizasPorDocumentoQuery(string Modulo, string Tipo, Guid DocumentoId, int Offset = 0, int Limit = 50)
    : IRequest<PagedResponse<PolizaPorDocumentoResponse>>;

public sealed record PolizaPorDocumentoResponse(Guid Id, string? Folio, DateOnly Fecha, EstadoPoliza Estado,
    Guid? PolizaAnuladaId, Guid? AnuladaPorPolizaId, Guid? PolizaRelacionadaId, RelacionPoliza? Relacion);

public sealed class PolizasPorDocumentoValidator : AbstractValidator<PolizasPorDocumentoQuery>
{
    public PolizasPorDocumentoValidator()
    {
        RuleFor(q => q.DocumentoId).NotEmpty();
        RuleFor(q => q.Offset).GreaterThanOrEqualTo(0);
        RuleFor(q => q.Limit).InclusiveBetween(1, 200);
        RuleFor(q => q).Must(q => (q.Modulo == "facturacion" && q.Tipo == "comprobante")
            || (q.Modulo == "cuentas_por_pagar" && q.Tipo == "factura_proveedor"))
            .WithMessage("El módulo y tipo de documento no están admitidos para esta consulta.");
    }
}

public sealed class PolizasPorDocumentoHandler(ContabilidadDbContext db)
    : IRequestHandler<PolizasPorDocumentoQuery, PagedResponse<PolizaPorDocumentoResponse>>
{
    public async Task<PagedResponse<PolizaPorDocumentoResponse>> Handle(PolizasPorDocumentoQuery request, CancellationToken cancellationToken)
    {
        var query = db.Polizas.AsNoTracking().Where(p => p.OrigenModulo == request.Modulo
            && p.OrigenTipo == request.Tipo && p.OrigenDocumentoId == request.DocumentoId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(p => p.Fecha).ThenBy(p => p.Id)
            .Skip(request.Offset).Take(request.Limit)
            .Select(p => new { p.Id, p.TipoClave, p.Numero, p.Fecha, p.Estado, p.PolizaAnuladaId,
                p.AnuladaPorPolizaId, p.PolizaRelacionadaId, p.Relacion })
            .ToListAsync(cancellationToken);
        return new PagedResponse<PolizaPorDocumentoResponse>([.. items.Select(p => new PolizaPorDocumentoResponse(p.Id,
            p.Numero is { } numero ? Poliza.FormatearFolio(p.TipoClave, numero) : null, p.Fecha, p.Estado,
            p.PolizaAnuladaId, p.AnuladaPorPolizaId, p.PolizaRelacionadaId, p.Relacion))], total, request.Offset, request.Limit);
    }
}
