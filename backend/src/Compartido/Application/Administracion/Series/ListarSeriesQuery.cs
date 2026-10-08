using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Series;

/// <summary>
/// Lista paginada de series (F-Admin-PR6.1). Filtros opcionales por
/// <see cref="EmpresaId"/> y <see cref="TipoDocumento"/>.
/// </summary>
public sealed record ListarSeriesQuery(
    Guid? EmpresaId = null,
    TipoDocumentoSerie? TipoDocumento = null,
    Guid? SucursalId = null,
    int Offset = 0,
    int Limit = 50) : IRequest<ListarSeriesResponse>;

public sealed record ListarSeriesResponse(
    IReadOnlyList<SerieResponse> Items,
    int Total);

public sealed class ListarSeriesHandler
    : IRequestHandler<ListarSeriesQuery, ListarSeriesResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;
    private readonly SerieSucursalScope _scope;

    public ListarSeriesHandler(CompartidoDbContext db, SerieSucursalScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<ListarSeriesResponse> Handle(
        ListarSeriesQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<Serie> q = _db.Series.AsNoTracking();
        if (query.EmpresaId is Guid empresaId)
            _scope.VerificarEmpresa(empresaId);
        if (!await _scope.PuedeGestionarGlobalesAsync(cancellationToken))
        {
            var autorizadas = await _scope.ListarAutorizadasAsync(cancellationToken);
            q = q.Where(s => s.SucursalId == null || autorizadas.Contains(s.SucursalId.Value));
        }
        if (query.EmpresaId is Guid empresaFiltro)
            q = q.Where(s => s.EmpresaId == empresaFiltro);
        if (query.TipoDocumento is TipoDocumentoSerie tipo)
            q = q.Where(s => s.TipoDocumento == tipo);
        if (query.SucursalId is Guid sucursalId)
        {
            await _scope.VerificarAsync(sucursalId, cancellationToken);
            q = q.Where(s => s.SucursalId == sucursalId || s.SucursalId == null);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(s => s.EmpresaId)
            .ThenBy(s => s.TipoDocumento)
            .ThenBy(s => s.Prefijo)
            .Skip(offset).Take(limit)
            .Select(s => new SerieResponse(
                s.Id, s.EmpresaId, s.SucursalId, s.TipoDocumento,
                s.Prefijo, s.Sufijo, s.ReinicioPeriodo, s.Activa, s.Version))
            .ToListAsync(cancellationToken);

        return new ListarSeriesResponse(items, total);
    }
}
