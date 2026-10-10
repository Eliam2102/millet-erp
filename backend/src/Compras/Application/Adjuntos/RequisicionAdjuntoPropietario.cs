using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compras.Application.Adjuntos;

public sealed class RequisicionAdjuntoPropietario(Millet.Compras.Infrastructure.ComprasDbContext db,
    ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales) : IAdjuntoPropietario
{
    public string TipoEntidad => "requisicion";
    public string CodigoNoEncontrado => "DOCUMENTO_NO_ENCONTRADO";
    public string PermisoVer => "compras.requisiciones.adjuntos-ver";
    public string PermisoSubir => "compras.requisiciones.adjuntos-subir";
    public string PermisoBaja => "compras.requisiciones.adjuntos-baja";
    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken)
        => await db.Requisiciones.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new AdjuntoPropietarioInfo(x.Id, x.EmpresaId, x.SucursalId,
                true, null, x.Folio.Valor )).FirstOrDefaultAsync(cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken)
        => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
        => SucursalScopeGuard.VerificarAsync(user.UserId,
            operacion == AdjuntoOperacion.Ver ? "compras.requisiciones.leer-todas-sucursales" : "compras.requisiciones.gestionar-todas-sucursales",
            permisos, (uid, c) => sucursales.EstaAsociadoAsync(uid, info.SucursalId!.Value, c), cancellationToken);
}
