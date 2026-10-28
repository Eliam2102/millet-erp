using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;

namespace Millet.CuentasPorCobrar.Application.Liberacion;

// C7.3 (ClickUp 86e3jvv6n): el panel de decidir necesita saber si la serie
// del pedido exige autorización (NuncaLibera) para seguir ofreciendo el
// override aunque la cobertura sea menor al 50 %. Solo lectura de las
// reglas activas; el backend sigue decidiendo con el prefijo más largo.

public sealed record ReglaLiberacionSerieResponse(string Prefijo, ComportamientoSerie Comportamiento);

public sealed record ListarReglasLiberacionSerieQuery : IRequest<IReadOnlyList<ReglaLiberacionSerieResponse>>;

public sealed class ListarReglasLiberacionSerieHandler
    : IRequestHandler<ListarReglasLiberacionSerieQuery, IReadOnlyList<ReglaLiberacionSerieResponse>>
{
    private readonly CuentasPorCobrarDbContext _db;
    public ListarReglasLiberacionSerieHandler(CuentasPorCobrarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ReglaLiberacionSerieResponse>> Handle(
        ListarReglasLiberacionSerieQuery query, CancellationToken cancellationToken) =>
        await _db.ReglasLiberacionSerie.AsNoTracking()
            .Where(r => r.Activo)
            .OrderBy(r => r.Prefijo)
            .Select(r => new ReglaLiberacionSerieResponse(r.Prefijo, r.Comportamiento))
            .ToListAsync(cancellationToken);
}
