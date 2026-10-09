using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.CuentasPorPagar.Application.Adjuntos;

public sealed class FacturaProveedorAdjuntoPropietario(Millet.CuentasPorPagar.Infrastructure.Persistence.CuentasPorPagarDbContext db,
    ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales) : IAdjuntoPropietario
{
    public string TipoEntidad => "factura_proveedor";
    public string CodigoNoEncontrado => "DOCUMENTO_NO_ENCONTRADO";
    public string PermisoVer => "cuentas_por_pagar.facturas.adjuntos-ver";
    public string PermisoSubir => "cuentas_por_pagar.facturas.adjuntos-subir";
    public string PermisoBaja => "cuentas_por_pagar.facturas.adjuntos-baja";
    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken)
        => await db.FacturasProveedor.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new AdjuntoPropietarioInfo(x.Id, x.EmpresaId, x.SucursalId,
                true, null, x.FolioProveedor ?? "Documento" )).FirstOrDefaultAsync(cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken)
        => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);
    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
        => SucursalScopeGuard.VerificarAsync(user.UserId,
            operacion == AdjuntoOperacion.Ver ? "cuentas_por_pagar.facturas.leer-todas-sucursales" : "cuentas_por_pagar.facturas.gestionar-todas-sucursales",
            permisos, (uid, c) => sucursales.EstaAsociadoAsync(uid, info.SucursalId!.Value, c), cancellationToken);
}
