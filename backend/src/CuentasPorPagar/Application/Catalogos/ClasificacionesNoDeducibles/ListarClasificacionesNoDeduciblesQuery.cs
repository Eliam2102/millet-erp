using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Catalogos.ClasificacionesNoDeducibles;

public sealed record ListarClasificacionesNoDeduciblesQuery : IRequest<IReadOnlyList<ClasificacionNoDeducibleResponse>>;

public sealed record ClasificacionNoDeducibleResponse(string Clave, string Descripcion);

public sealed class ListarClasificacionesNoDeduciblesHandler
    : IRequestHandler<ListarClasificacionesNoDeduciblesQuery, IReadOnlyList<ClasificacionNoDeducibleResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    public ListarClasificacionesNoDeduciblesHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<ClasificacionNoDeducibleResponse>> Handle(
        ListarClasificacionesNoDeduciblesQuery query, CancellationToken cancellationToken) =>
        await _db.ClasificacionesNoDeducibles.AsNoTracking().Where(c => c.Activa).OrderBy(c => c.Descripcion)
            .Select(c => new ClasificacionNoDeducibleResponse(c.Clave, c.Descripcion)).ToListAsync(cancellationToken);
}
