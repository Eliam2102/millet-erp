using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;

namespace Millet.Identidad.Application.Permisos;

/// <summary>
/// Lista el catálogo canónico de permisos del sistema (F-Admin-PR3.2).
/// Si <see cref="AgrupadoPorModulo"/> es <c>true</c>, también retorna
/// <see cref="ListarPermisosResponse.Grupos"/> con los permisos agrupados
/// por módulo (UI matriz). En caso contrario solo retorna la lista plana.
/// </summary>
public sealed record ListarPermisosQuery(
    bool? AgrupadoPorModulo = null) : IRequest<ListarPermisosResponse>;

public sealed record PermisoResponse(
    Guid Id,
    string Codigo,
    string Modulo,
    string Recurso,
    string Accion,
    string Descripcion);

public sealed record PermisosPorModulo(
    string Modulo,
    IReadOnlyList<PermisoResponse> Items);

public sealed record ListarPermisosResponse(
    IReadOnlyList<PermisoResponse> Items,
    IReadOnlyList<PermisosPorModulo>? Grupos);

public sealed class ListarPermisosHandler
    : IRequestHandler<ListarPermisosQuery, ListarPermisosResponse>
{
    private readonly IdentidadDbContext _db;

    public ListarPermisosHandler(IdentidadDbContext db) => _db = db;

    public async Task<ListarPermisosResponse> Handle(
        ListarPermisosQuery query, CancellationToken cancellationToken)
    {
        var items = await _db.Permisos.AsNoTracking()
            .OrderBy(p => p.Modulo).ThenBy(p => p.Recurso).ThenBy(p => p.Accion)
            .Select(p => new PermisoResponse(
                p.Id, p.Codigo, p.Modulo, p.Recurso, p.Accion, p.Descripcion))
            .ToListAsync(cancellationToken);

        IReadOnlyList<PermisosPorModulo>? grupos = null;
        if (query.AgrupadoPorModulo is true)
        {
            grupos = items
                .GroupBy(p => p.Modulo)
                .Select(g => new PermisosPorModulo(g.Key, g.ToList()))
                .ToList();
        }

        return new ListarPermisosResponse(items, grupos);
    }
}
