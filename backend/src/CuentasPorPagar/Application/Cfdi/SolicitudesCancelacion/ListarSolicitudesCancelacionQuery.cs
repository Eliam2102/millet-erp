using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

public sealed record ListarSolicitudesCancelacionQuery(Guid CfdiId) : IRequest<IReadOnlyList<SolicitudCancelacionResponse>>;

public sealed class ListarSolicitudesCancelacionHandler
    : IRequestHandler<ListarSolicitudesCancelacionQuery, IReadOnlyList<SolicitudCancelacionResponse>>
{
    private readonly CuentasPorPagarDbContext _db;

    public ListarSolicitudesCancelacionHandler(CuentasPorPagarDbContext db) { _db = db; }

    public async Task<IReadOnlyList<SolicitudCancelacionResponse>> Handle(
        ListarSolicitudesCancelacionQuery query, CancellationToken cancellationToken)
    {
        var filas = await _db.SolicitudesCancelacionCfdi.AsNoTracking()
            .Where(s => s.CfdiRecibidoId == query.CfdiId)
            .OrderByDescending(s => s.FechaRecepcion)
            .ToListAsync(cancellationToken);
        return filas.Select(SolicitudCancelacionResponse.De).ToList();
    }
}
