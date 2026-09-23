using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.UsuarioSucursales;

/// <summary>
/// Lista los usuarios asignados a una sucursal (F1-ADM-01 Fase 2).
/// Análogo de <c>ListarDepartamentosDeSucursalQuery</c> pero para
/// <see cref="Millet.Identidad.Domain.UsuarioSucursal"/>.
///
/// <para>404 <c>SUCURSAL_NO_ENCONTRADA</c> si la sucursal no existe.</para>
/// <para>
/// 403 <c>SUCURSAL_NO_ASOCIADA</c> (guard de pertenencia, Fase 2 sección
/// C) si el usuario autenticado no está asociado a la sucursal y no
/// tiene el permiso <c>admin.sucursales.usuarios-gestionar</c>. A
/// diferencia de los handlers de Compartido (que resuelven la
/// pertenencia vía <see cref="IUsuarioSucursalReadPort"/>), este handler
/// vive en el mismo módulo que <c>UsuarioSucursal</c> y consulta
/// directo su propio DbContext.
/// </para>
/// </summary>
public sealed record ListarUsuariosPorSucursalQuery(Guid SucursalId)
    : IRequest<ListarUsuariosPorSucursalResponse>;

public sealed record ListarUsuariosPorSucursalResponse(
    IReadOnlyList<UsuarioSucursalResponse> Items,
    int Total);

public sealed class ListarUsuariosPorSucursalHandler
    : IRequestHandler<ListarUsuariosPorSucursalQuery, ListarUsuariosPorSucursalResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICurrentUserPermissions _permisos;

    public ListarUsuariosPorSucursalHandler(
        IdentidadDbContext db,
        ICurrentUserContext currentUser,
        ICurrentUserPermissions permisos)
    {
        _db = db;
        _currentUser = currentUser;
        _permisos = permisos;
    }

    public async Task<ListarUsuariosPorSucursalResponse> Handle(
        ListarUsuariosPorSucursalQuery query, CancellationToken cancellationToken)
    {
        var sucursalExiste = await _db.Set<Sucursal>().AsNoTracking()
            .AnyAsync(s => s.Id == query.SucursalId, cancellationToken);
        if (!sucursalExiste)
        {
            throw new EntityNotFoundException(
                "SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{query.SucursalId}'.");
        }

        await SucursalScopeGuard.VerificarAsync(
            _currentUser.UserId,
            SucursalScopeGuardPermisos.UsuariosGestionar,
            _permisos,
            (userId, ct) => _db.UsuarioSucursales.AsNoTracking().AnyAsync(
                a => a.UsuarioId == userId
                  && a.SucursalId == query.SucursalId
                  && a.Estatus == EstatusCatalogo.Activo,
                ct),
            cancellationToken);

        var items = await (
            from a in _db.UsuarioSucursales.AsNoTracking()
            join u in _db.Usuarios.AsNoTracking()
                on a.UsuarioId equals u.Id
            where a.SucursalId == query.SucursalId
            orderby u.Email
            select new UsuarioSucursalResponse(
                a.SucursalId,
                a.UsuarioId,
                u.Email,
                u.Nombre,
                a.Estatus,
                a.Version)
        ).ToListAsync(cancellationToken);

        return new ListarUsuariosPorSucursalResponse(items, items.Count);
    }
}
