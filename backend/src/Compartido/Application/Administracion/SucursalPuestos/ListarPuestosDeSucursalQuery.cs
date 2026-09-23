using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.Application.SucursalPuestos;

/// <summary>
/// Lista los puestos asignados a una sucursal (F1-ADM-01 Fase 2). Análogo
/// exacto de <c>ListarDepartamentosDeSucursalQuery</c>. Devuelve SOLO las
/// asignaciones existentes con su estatus en la sucursal — el frontend
/// hace un segundo fetch al catálogo global de Puestos si necesita
/// mostrar también los no asignados como agregables.
///
/// <para>404 <c>SUCURSAL_NO_ENCONTRADA</c> si la sucursal no existe.</para>
/// <para>
/// 403 <c>SUCURSAL_NO_ASOCIADA</c> (guard de pertenencia, Fase 2 sección
/// C) si el usuario autenticado no está asociado a la sucursal y no
/// tiene el permiso <c>admin.sucursales.puestos-gestionar</c>.
/// </para>
/// </summary>
public sealed record ListarPuestosDeSucursalQuery(Guid SucursalId)
    : IRequest<ListarPuestosDeSucursalResponse>;

public sealed record ListarPuestosDeSucursalResponse(
    IReadOnlyList<SucursalPuestoResponse> Items,
    int Total);

public sealed class ListarPuestosDeSucursalHandler
    : IRequestHandler<ListarPuestosDeSucursalQuery, ListarPuestosDeSucursalResponse>
{
    private readonly CompartidoDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;
    private readonly IUsuarioSucursalReadPort _usuarioSucursal;

    public ListarPuestosDeSucursalHandler(
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

    public async Task<ListarPuestosDeSucursalResponse> Handle(
        ListarPuestosDeSucursalQuery query, CancellationToken cancellationToken)
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
            SucursalScopeGuardPermisos.PuestosGestionar,
            _permisos,
            (userId, ct) => _usuarioSucursal.EstaAsociadoAsync(userId, query.SucursalId, ct),
            cancellationToken);

        var items = await (
            from a in _db.SucursalPuestos.AsNoTracking()
            join p in _db.Puestos.AsNoTracking()
                on a.PuestoId equals p.Id
            join d in _db.Departamentos.AsNoTracking()
                on a.DepartamentoId equals d.Id into deptos
            from d in deptos.DefaultIfEmpty()
            where a.SucursalId == query.SucursalId
            orderby p.Clave
            select new SucursalPuestoResponse(
                a.SucursalId,
                a.PuestoId,
                p.Clave,
                p.Nombre,
                a.DepartamentoId,
                d != null ? d.Nombre : null,
                a.Estatus,
                a.Version)
        ).ToListAsync(cancellationToken);

        return new ListarPuestosDeSucursalResponse(items, items.Count);
    }
}
