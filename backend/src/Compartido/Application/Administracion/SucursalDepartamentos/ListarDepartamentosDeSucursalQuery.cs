using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalDepartamentos;

/// <summary>
/// Lista los departamentos asignados a una sucursal (PR-A1). Devuelve
/// SOLO las asignaciones existentes con su estatus en la sucursal — el
/// frontend hace un segundo fetch al catálogo global de Departamentos si
/// necesita mostrar también los no asignados como agregables.
///
/// <para>404 <c>SUCURSAL_NO_ENCONTRADA</c> si la sucursal no existe.</para>
/// <para>
/// 403 <c>SUCURSAL_NO_ASOCIADA</c> (guard de pertenencia, F1-ADM-01
/// Fase 2 sección C) si el usuario autenticado no está asociado a la
/// sucursal y no tiene el permiso
/// <c>admin.sucursales.departamentos-gestionar</c>.
/// </para>
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
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuarioSucursalReadPort _usuarioSucursal;

    public ListarDepartamentosDeSucursalHandler(
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

        await SucursalScopeGuard.VerificarAsync(
            _currentUser.UserId,
            SucursalScopeGuardPermisos.DepartamentosGestionar,
            _permisos,
            (userId, ct) => _usuarioSucursal.EstaAsociadoAsync(userId, query.SucursalId, ct),
            cancellationToken);

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
