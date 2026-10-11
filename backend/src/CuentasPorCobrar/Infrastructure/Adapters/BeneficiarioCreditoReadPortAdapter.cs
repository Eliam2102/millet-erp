using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.Identidad.Application;
using Millet.Identidad.Domain;
using Millet.Identidad.Infrastructure;
namespace Millet.CuentasPorCobrar.Infrastructure.Adapters;
public sealed class BeneficiarioCreditoReadPortAdapter(IdentidadDbContext db, IPermissionLoader permisos) : IBeneficiarioCreditoReadPort
{
    public async Task<bool> PuedeDecidirAsync(Guid usuarioId, Guid empresaId, CancellationToken ct) =>
        (await permisos.LoadForUserInEmpresaAsync(usuarioId, empresaId, ct)).Contains(PermisosCanonicos.CuentasPorCobrarLiberacionDecidir);
    public async Task<IReadOnlyList<BeneficiarioCreditoDto>> ListarAsync(Guid empresaId, CancellationToken ct)
    {
        var usuarios = await db.Usuarios.AsNoTracking().Where(u => u.Activo && db.UsuarioEmpresaRoles.Any(r => r.UsuarioId == u.Id && r.EmpresaId == empresaId))
            .OrderBy(u => u.Nombre).Select(u => new BeneficiarioCreditoDto(u.Id, u.Nombre, u.Email)).ToListAsync(ct);
        var elegibles = new List<BeneficiarioCreditoDto>();
        // El cargador canónico incluye roles de grupos y excepciones conceder/denegar.
        foreach (var usuario in usuarios)
            if (await PuedeDecidirAsync(usuario.Id, empresaId, ct)) elegibles.Add(usuario);
        return elegibles;
    }
}
