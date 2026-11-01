using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// Adapter de <see cref="IDestinatariosAvisoCsdPort"/> (F6.9) sobre Identidad:
/// usuarios activos con alguno de los roles en la empresa. Vive en Api porque
/// Integraciones.Fiscal no referencia Identidad.
/// </summary>
public sealed class DestinatariosAvisoCsdAdapter(
    IdentidadDbContext db,
    ICurrentEmpresaContext empresaContext) : IDestinatariosAvisoCsdPort
{
    public async Task<IReadOnlyList<DestinatarioAvisoCsd>> ObtenerAsync(
        Guid empresaId, IReadOnlyList<string> codigosRol, CancellationToken cancellationToken)
    {
        if (codigosRol.Count == 0) return [];
        using var bypass = empresaContext.Bypass();

        return await (
            from asignacion in db.UsuarioEmpresaRoles.AsNoTracking()
            join rol in db.Roles.AsNoTracking() on asignacion.RolId equals rol.Id
            join usuario in db.Usuarios.AsNoTracking() on asignacion.UsuarioId equals usuario.Id
            where asignacion.EmpresaId == empresaId && codigosRol.Contains(rol.Codigo) && rol.Activo && usuario.Activo
            orderby usuario.Nombre
            select new DestinatarioAvisoCsd(usuario.Nombre, usuario.Email))
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
