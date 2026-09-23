using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Administracion.Application.Departamentos;

/// <summary>
/// <see cref="SucursalId"/> (F1-ADM-01 Fase 2): filtro opcional que
/// restringe el catálogo a los departamentos asignados (vía
/// <c>SucursalDepartamento</c> Activo) a esa sucursal. Cuando viene, el
/// handler aplica el mismo guard de pertenencia que
/// <c>ListarDepartamentosDeSucursalQuery</c> — ver
/// <see cref="SucursalScopeGuard"/>.
/// </summary>
public sealed record ListarDepartamentosQuery(
    int Offset = 0,
    int Limit = 50,
    string? Query = null,
    Millet.Catalogos.Domain.EstatusCatalogo? Estatus = null,
    Guid? SucursalId = null)
    : IRequest<ListarDepartamentosResponse>;

public sealed record ListarDepartamentosResponse(
    IReadOnlyList<DepartamentoResponse> Items,
    int Total,
    int Offset,
    int Limit);

public sealed class ListarDepartamentosHandler
    : IRequestHandler<ListarDepartamentosQuery, ListarDepartamentosResponse>
{
    private const int LimitMax = 200;
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuarioSucursalReadPort _usuarioSucursal;

    public ListarDepartamentosHandler(
        CompartidoDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos,
        IUsuarioSucursalReadPort usuarioSucursal)
    {
        _db = db;
        _currentUser = currentUser;
        _permisos = permisos;
        _usuarioSucursal = usuarioSucursal;
    }

    public async Task<ListarDepartamentosResponse> Handle(
        ListarDepartamentosQuery request,
        CancellationToken cancellationToken)
    {
        var offset = Math.Max(0, request.Offset);
        var limit = request.Limit is <= 0 or > LimitMax ? 50 : request.Limit;
        var query = _db.Departamentos.AsNoTracking();

        if (request.Estatus is { } estatus)
        {
            query = query.Where(d => d.Estatus == estatus);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var text = request.Query.Trim();
            query = query.Where(d => d.Clave.Contains(text) || d.Nombre.Contains(text));
        }

        if (request.SucursalId is { } sucursalId)
        {
            await SucursalScopeGuard.VerificarAsync(
                _currentUser.UserId,
                SucursalScopeGuardPermisos.DepartamentosGestionar,
                _permisos,
                (userId, ct) => _usuarioSucursal.EstaAsociadoAsync(userId, sucursalId, ct),
                cancellationToken);

            query = query.Where(d => _db.SucursalDepartamentos.Any(a =>
                a.SucursalId == sucursalId
                && a.DepartamentoId == d.Id
                && a.Estatus == Millet.Catalogos.Domain.EstatusCatalogo.Activo));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(d => d.Clave)
            .Skip(offset)
            .Take(limit)
            .Select(d => new DepartamentoResponse(
                d.Id, d.Clave, d.Nombre, d.Estatus, d.Version))
            .ToListAsync(cancellationToken);

        return new ListarDepartamentosResponse(items, total, offset, limit);
    }
}
