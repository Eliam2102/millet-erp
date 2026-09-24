using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Identidad.Application.Usuarios;

/// <summary>
/// Detalle de un usuario con sus asignaciones expandidas (F-Admin-PR4.2).
/// Cada asignación trae <c>Empresa.Rfc</c> y <c>Rol.Codigo</c> resueltos
/// para que la UI no tenga que joinear contra catálogos.
///
/// 404 <c>USUARIO_NO_ENCONTRADO</c> si no existe.
/// </summary>
public sealed record ObtenerUsuarioQuery(Guid Id) : IRequest<UsuarioDetalleResponse>;

public sealed class ObtenerUsuarioHandler
    : IRequestHandler<ObtenerUsuarioQuery, UsuarioDetalleResponse>
{
    private readonly IdentidadDbContext _db;
    private readonly CompartidoDbContext _compartido;
    private readonly ICurrentEmpresaContext _empresaContext;

    public ObtenerUsuarioHandler(
        IdentidadDbContext db,
        CompartidoDbContext compartido,
        ICurrentEmpresaContext empresaContext)
    {
        _db = db;
        _compartido = compartido;
        _empresaContext = empresaContext;
    }

    public async Task<UsuarioDetalleResponse> Handle(
        ObtenerUsuarioQuery query, CancellationToken cancellationToken)
    {
        // El detalle admin de usuario debe mostrar TODAS sus asignaciones
        // cross-empresa, no solo las de la empresa actual. Endpoint gated
        // por identidad.usuarios.leer (RBAC). Mismo razonamiento que en
        // AsignarRolAUsuarioHandler.
        using var bypass = _empresaContext.Bypass();

        var usuario = await _db.Usuarios.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == query.Id, cancellationToken)
            ?? throw new EntityNotFoundException(
                "USUARIO_NO_ENCONTRADO",
                $"No existe usuario con id '{query.Id}'.");

        // Join cross-schema: Empresa vive en compartido pero está mapeada
        // en IdentidadDbContext como referencia (ver
        // ConfigureEmpresaReference). Rol vive en identidad.
        var asignaciones = await (
            from uer in _db.UsuarioEmpresaRoles.AsNoTracking()
            join e in _db.Set<Empresa>().AsNoTracking() on uer.EmpresaId equals e.Id
            join r in _db.Roles.AsNoTracking() on uer.RolId equals r.Id
            where uer.UsuarioId == query.Id
            orderby e.Rfc, r.Codigo
            select new AsignacionDetalleResponse(
                uer.Id,
                uer.EmpresaId,
                e.Rfc,
                uer.RolId,
                r.Codigo,
                uer.CreatedAt))
            .ToListAsync(cancellationToken);

        var empleadoId = await _compartido.Empleados.AsNoTracking()
            .Where(e => e.UsuarioId == usuario.Id)
            .OrderBy(e => e.Id)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var dto = new UsuarioResponse(
            usuario.Id,
            usuario.Email,
            usuario.EntraOid,
            usuario.Nombre,
            usuario.DepartamentoId,
            usuario.Activo,
            usuario.Version,
            empleadoId);

        return new UsuarioDetalleResponse(dto, asignaciones);
    }
}
