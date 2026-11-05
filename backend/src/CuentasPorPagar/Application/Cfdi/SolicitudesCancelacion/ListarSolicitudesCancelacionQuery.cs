using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.Ports.Administracion;
using Millet.CuentasPorPagar.Infrastructure.Persistence;

namespace Millet.CuentasPorPagar.Application.Cfdi.SolicitudesCancelacion;

public sealed record ListarSolicitudesCancelacionQuery(Guid CfdiId) : IRequest<IReadOnlyList<SolicitudCancelacionResponse>>;

public sealed class ListarSolicitudesCancelacionHandler
    : IRequestHandler<ListarSolicitudesCancelacionQuery, IReadOnlyList<SolicitudCancelacionResponse>>
{
    private readonly CuentasPorPagarDbContext _db;
    private readonly IUsuarioNombreReadPort _usuarios;

    public ListarSolicitudesCancelacionHandler(CuentasPorPagarDbContext db, IUsuarioNombreReadPort usuarios)
    {
        _db = db; _usuarios = usuarios;
    }

    public async Task<IReadOnlyList<SolicitudCancelacionResponse>> Handle(
        ListarSolicitudesCancelacionQuery query, CancellationToken cancellationToken)
    {
        var filas = await _db.SolicitudesCancelacionCfdi.AsNoTracking()
            .Where(s => s.CfdiRecibidoId == query.CfdiId)
            .OrderByDescending(s => s.FechaRecepcion)
            .ToListAsync(cancellationToken);
        var ids = filas.SelectMany(s => new[] { s.ResponsableId, s.DecididoPor })
            .Where(i => i.HasValue).Select(i => i!.Value).ToArray();
        var nombres = await _usuarios.ObtenerNombresAsync(ids, cancellationToken);
        string? Nombre(Guid? id) => id.HasValue && nombres.TryGetValue(id.Value, out var n) ? n : null;
        return filas.Select(s => SolicitudCancelacionResponse.De(s) with
        {
            ResponsableNombre = Nombre(s.ResponsableId),
            DecididoPorNombre = Nombre(s.DecididoPor),
        }).ToList();
    }
}
