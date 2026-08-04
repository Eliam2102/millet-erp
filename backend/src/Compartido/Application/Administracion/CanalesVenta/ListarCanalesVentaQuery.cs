using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.CanalesVenta;

/// <summary>
/// Lista el catálogo completo de canales de venta ordenado por id
/// (FAC-ING-PR2). Catálogo corto (decenas de filas como máximo) — sin
/// paginación, con filtro opcional por estatus para la pantalla de admin.
/// </summary>
public sealed record ListarCanalesVentaQuery(
    EstatusCatalogo? Estatus = null) : IRequest<IReadOnlyList<CanalVentaResponse>>;

public sealed class ListarCanalesVentaHandler
    : IRequestHandler<ListarCanalesVentaQuery, IReadOnlyList<CanalVentaResponse>>
{
    private readonly CompartidoDbContext _db;

    public ListarCanalesVentaHandler(CompartidoDbContext db) => _db = db;

    public async Task<IReadOnlyList<CanalVentaResponse>> Handle(
        ListarCanalesVentaQuery query, CancellationToken cancellationToken)
    {
        var canales = _db.CanalesVenta.AsNoTracking();
        if (query.Estatus is EstatusCatalogo estatus)
            canales = canales.Where(c => c.Estatus == estatus);

        return await canales
            .OrderBy(c => c.Id)
            .Select(c => new CanalVentaResponse(c.Id, c.Nombre, c.Estatus, c.Version, c.ClaveAw))
            .ToListAsync(cancellationToken);
    }
}
