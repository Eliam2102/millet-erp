using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.UsuarioSucursales;

/// <summary>Asignaciones de un usuario dentro de la empresa activa del administrador.</summary>
public sealed record ListarSucursalesDeUsuarioQuery(Guid UsuarioId)
    : IRequest<ListarUsuariosPorSucursalResponse>;

public sealed class ListarSucursalesDeUsuarioHandler
    : IRequestHandler<ListarSucursalesDeUsuarioQuery, ListarUsuariosPorSucursalResponse>
{
    private readonly IdentidadDbContext _db;

    public ListarSucursalesDeUsuarioHandler(IdentidadDbContext db) => _db = db;

    public async Task<ListarUsuariosPorSucursalResponse> Handle(
        ListarSucursalesDeUsuarioQuery query, CancellationToken cancellationToken)
    {
        var usuario = await _db.Usuarios.AsNoTracking()
            .Where(u => u.Id == query.UsuarioId)
            .Select(u => new { u.Email, u.Nombre })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException("USUARIO_NO_ENCONTRADO", "El usuario no existe.");

        // Sucursal lleva el filtro de empresa activa. Nunca se devuelven
        // asignaciones de otra razón social aunque el usuario tenga varias.
        var items = await (
            from a in _db.UsuarioSucursales.AsNoTracking()
            join s in _db.Set<Sucursal>().AsNoTracking() on a.SucursalId equals s.Id
            where a.UsuarioId == query.UsuarioId
            orderby s.Nombre
            select new UsuarioSucursalResponse(
                a.SucursalId, a.UsuarioId, usuario.Email, usuario.Nombre,
                a.Estatus, a.Version)
        ).ToListAsync(cancellationToken);

        return new ListarUsuariosPorSucursalResponse(items, items.Count);
    }
}
