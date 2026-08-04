using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Application.RfcsReceptores.ListarRfcsReceptores;

public sealed record ListarRfcsReceptoresQuery(Guid EmpresaId)
    : IRequest<IReadOnlyList<RfcReceptorResponse>>;

public sealed class ListarRfcsReceptoresHandler
    : IRequestHandler<ListarRfcsReceptoresQuery, IReadOnlyList<RfcReceptorResponse>>
{
    private readonly IntegracionesFiscalDbContext _db;
    private readonly IClock _clock;

    public ListarRfcsReceptoresHandler(IntegracionesFiscalDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<IReadOnlyList<RfcReceptorResponse>> Handle(
        ListarRfcsReceptoresQuery query, CancellationToken cancellationToken)
    {
        var list = await _db.RfcsReceptores
            .AsNoTracking()
            .Where(r => r.EmpresaId == query.EmpresaId)
            .OrderBy(r => r.Rfc)
            .ToListAsync(cancellationToken);

        var ahora = _clock.UtcNow;
        return list.Select(r => r.ToResponse(ahora)).ToList();
    }
}
