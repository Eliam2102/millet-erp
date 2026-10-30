using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Infrastructure;
using Millet.Tesoreria.Domain.Ports;

namespace Millet.Tesoreria.Infrastructure.Adapters;

/// <summary>Adapter de <see cref="IFirmanteRolesReadPort"/>: roles activos del usuario en la empresa (Identidad, solo lectura).</summary>
public sealed class FirmanteRolesReadAdapter(IdentidadDbContext db) : IFirmanteRolesReadPort
{
    public async Task<IReadOnlyCollection<string>> CodigosRolAsync(Guid usuarioId, Guid empresaId, CancellationToken cancellationToken)
    {
        var codigos = await (from ur in db.UsuarioEmpresaRoles.AsNoTracking()
                             join r in db.Roles.AsNoTracking() on ur.RolId equals r.Id
                             where ur.UsuarioId == usuarioId && ur.EmpresaId == empresaId && r.Activo
                             select r.Codigo).ToListAsync(cancellationToken);
        return codigos.Select(c => c.ToLowerInvariant()).ToList();
    }
}
