using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.Roles;

/// <summary>
/// Lista paginada de roles (F-Admin-PR3.2). Filtro opcional por
/// <see cref="SoloActivos"/> = <c>true</c> oculta soft-deleted.
/// </summary>
public sealed record ListarRolesQuery(
    int Offset = 0,
    int Limit = 50,
    bool? SoloActivos = null) : IRequest<ListarRolesResponse>;

public sealed record ListarRolesResponse(
    IReadOnlyList<RolResponse> Items,
    int Total);

public sealed class ListarRolesHandler
    : IRequestHandler<ListarRolesQuery, ListarRolesResponse>
{
    private const int LimitMax = 200;
    private readonly IdentidadDbContext _db;

    public ListarRolesHandler(IdentidadDbContext db) => _db = db;

    public async Task<ListarRolesResponse> Handle(
        ListarRolesQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        var q = _db.Roles.AsNoTracking();
        if (query.SoloActivos is true)
        {
            q = q.Where(r => r.Activo);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(r => r.Codigo)
            .Skip(offset).Take(limit)
            .Select(r => new RolResponse(
                r.Id, r.Codigo, r.Nombre, r.Descripcion,
                r.EsDelSistema, r.Activo, r.Version))
            .ToListAsync(cancellationToken);

        return new ListarRolesResponse(items, total);
    }
}
