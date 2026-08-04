using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;

namespace Millet.Administracion.Application.Empresas;

/// <summary>
/// Lista paginada de empresas (F-Admin-PR2.3). Filtro opcional por
/// <see cref="SoloActivas"/> = <c>true</c> oculta inactivas.
/// </summary>
public sealed record ListarEmpresasQuery(
    int Offset = 0,
    int Limit = 50,
    bool? SoloActivas = null) : IRequest<ListarEmpresasResponse>;

public sealed record ListarEmpresasResponse(
    IReadOnlyList<EmpresaResponse> Items,
    int Total);

public sealed class ListarEmpresasHandler
    : IRequestHandler<ListarEmpresasQuery, ListarEmpresasResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;

    public ListarEmpresasHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarEmpresasResponse> Handle(
        ListarEmpresasQuery query, CancellationToken cancellationToken)
    {
        var offset = query.Offset < 0 ? 0 : query.Offset;
        var limit = query.Limit is <= 0 or > LimitMax
            ? Math.Min(50, LimitMax)
            : query.Limit;

        IQueryable<Millet.Administracion.Domain.Empresa> q = _db.Empresas.AsNoTracking();
        if (query.SoloActivas is true)
        {
            q = q.Where(e => e.Activa);
        }

        var total = await q.CountAsync(cancellationToken);
        var items = await q
            .OrderBy(e => e.Rfc)
            .Skip(offset).Take(limit)
            .Select(e => new EmpresaResponse(
                e.Id, e.Rfc, e.RazonSocial, e.NombreComercial,
                e.RegimenFiscal, e.TasaIvaDefault, e.CodigoPostal, e.Activa, e.Version))
            .ToListAsync(cancellationToken);

        return new ListarEmpresasResponse(items, total);
    }
}
