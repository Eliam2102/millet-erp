using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalDepartamentos;

/// <summary>
/// Lista los departamentos asignados a una sucursal (PR-A1). Devuelve
/// SOLO las asignaciones existentes con su estatus en la sucursal — el
/// frontend hace un segundo fetch al catálogo global de Departamentos si
/// necesita mostrar también los no asignados como agregables.
///
/// <para>404 <c>SUCURSAL_NO_ENCONTRADA</c> si la sucursal no existe.</para>
/// </summary>
public sealed record ListarDepartamentosDeSucursalQuery(Guid SucursalId)
    : IRequest<ListarDepartamentosDeSucursalResponse>;

public sealed record ListarDepartamentosDeSucursalResponse(
    IReadOnlyList<SucursalDepartamentoResponse> Items,
    int Total);

public sealed class ListarDepartamentosDeSucursalHandler
    : IRequestHandler<ListarDepartamentosDeSucursalQuery, ListarDepartamentosDeSucursalResponse>
{
    private readonly CompartidoDbContext _db;

    public ListarDepartamentosDeSucursalHandler(CompartidoDbContext db) => _db = db;

    public async Task<ListarDepartamentosDeSucursalResponse> Handle(
        ListarDepartamentosDeSucursalQuery query, CancellationToken cancellationToken)
    {
        var sucursalExiste = await _db.Sucursales.AsNoTracking()
            .AnyAsync(s => s.Id == query.SucursalId, cancellationToken);
        if (!sucursalExiste)
        {
            throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{query.SucursalId}'.");
        }

        var items = await (
            from a in _db.SucursalDepartamentos.AsNoTracking()
            join d in _db.Departamentos.AsNoTracking()
                on a.DepartamentoId equals d.Id
            where a.SucursalId == query.SucursalId
            orderby d.Clave
            select new SucursalDepartamentoResponse(
                a.SucursalId,
                a.DepartamentoId,
                d.Clave,
                d.Nombre,
                a.Estatus,
                a.Version)
        ).ToListAsync(cancellationToken);

        return new ListarDepartamentosDeSucursalResponse(items, items.Count);
    }
}
