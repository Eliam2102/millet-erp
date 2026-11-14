using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;

namespace Millet.Contabilidad.Application.Periodos;

/// <summary>Reapertura acotada de un periodo (M-213) para elegirla en pantalla (H-U29): sin pegar su identificador.</summary>
public sealed record ReaperturaPeriodoResponse(
    Guid Id, int Anio, int Numero, string Periodo, string EvidenciaReferencia, DateTimeOffset CreadaEn, string CreadaPor,
    DateTimeOffset VenceEn, bool Vigente, DateTimeOffset? CerradaEn);

/// <summary>Reaperturas de un periodo, la más reciente primero (vigentes y concluidas: una incidencia se resuelve con cualquiera).</summary>
public sealed record ListarReaperturasPeriodoQuery(int Anio, int Numero) : IRequest<IReadOnlyList<ReaperturaPeriodoResponse>>;

public sealed class ListarReaperturasPeriodoValidator : AbstractValidator<ListarReaperturasPeriodoQuery>
{
    public ListarReaperturasPeriodoValidator()
    {
        RuleFor(q => q.Anio).InclusiveBetween(2000, 2100);
        RuleFor(q => q.Numero).InclusiveBetween(1, 13);
    }
}

public sealed class ListarReaperturasPeriodoHandler(ContabilidadDbContext db)
    : IRequestHandler<ListarReaperturasPeriodoQuery, IReadOnlyList<ReaperturaPeriodoResponse>>
{
    public const int Maximo = 50;

    public async Task<IReadOnlyList<ReaperturaPeriodoResponse>> Handle(ListarReaperturasPeriodoQuery request, CancellationToken cancellationToken)
    {
        // Filtro global de empresa (IPerteneceAEmpresa): solo las de la empresa actual.
        var filas = await db.Reaperturas.AsNoTracking()
            .Where(r => r.Anio == request.Anio && r.Numero == request.Numero)
            .OrderByDescending(r => r.CreadaEn).Take(Maximo)
            .ToListAsync(cancellationToken);
        return filas.Select(r => new ReaperturaPeriodoResponse(
            r.Id, r.Anio, r.Numero, PeriodoContable.Etiqueta(r.Anio, r.Numero), r.EvidenciaReferencia, r.CreadaEn, r.CreadaPor,
            r.VenceEn, r.Vigente, r.CerradaEn)).ToList();
    }
}
