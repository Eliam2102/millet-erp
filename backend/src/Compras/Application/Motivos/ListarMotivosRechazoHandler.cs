using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compras.Infrastructure;

namespace Millet.Compras.Application.Motivos;

public sealed class ListarMotivosRechazoHandler : IRequestHandler<ListarMotivosRechazoQuery, IReadOnlyList<MotivoRechazoResponse>>
{
    private readonly ComprasDbContext _db;

    public ListarMotivosRechazoHandler(ComprasDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MotivoRechazoResponse>> Handle(
        ListarMotivosRechazoQuery query,
        CancellationToken cancellationToken)
    {
        var motivos = await _db.MotivosRechazo
            .AsNoTracking()
            .Where(m => m.Activo)
            .OrderBy(m => m.Clave)
            .Select(m => new MotivoRechazoResponse(
                m.Id,
                m.Clave,
                m.Descripcion,
                m.PermiteTextoLibre,
                m.AplicaA))
            .ToListAsync(cancellationToken);

        return motivos;
    }
}
