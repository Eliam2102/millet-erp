using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Adjuntos;

/// <summary>
/// Propietario del PDF de una cotización de Compras (ADR-0058). El alcance por
/// sucursal es el de la requisición (ADR-0051); el archivo no se da de baja
/// una vez que la cotización se eligió para una OC.
/// </summary>
public sealed class CotizacionAdjuntoPropietario(Millet.Compras.Infrastructure.ComprasDbContext db,
    ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales) : IAdjuntoPropietario
{
    public string TipoEntidad => "cotizacion_compra";
    public string CodigoNoEncontrado => "COTIZACION_NO_ENCONTRADA";
    public string PermisoVer => "compras.cotizaciones.leer";
    public string PermisoSubir => "compras.cotizaciones.gestionar";
    public string PermisoBaja => "compras.cotizaciones.gestionar";

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken)
        => await db.Cotizaciones.AsNoTracking().Where(x => x.Id == entidadId)
            .Select(x => new AdjuntoPropietarioInfo(x.Id, x.EmpresaId, x.SucursalId,
                x.OrdenCompraId == null, null, x.FolioProveedor ?? "Cotización")).FirstOrDefaultAsync(cancellationToken);

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken)
        => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
    {
        if (operacion == AdjuntoOperacion.Baja && !info.PuedeSubir)
        {
            throw new BusinessRuleException("COT_COMPARATIVO_CERRADO",
                "La cotización ya se eligió para una orden de compra; su archivo no se puede dar de baja.");
        }

        return SucursalScopeGuard.VerificarAsync(user.UserId,
            operacion == AdjuntoOperacion.Ver ? "compras.requisiciones.leer-todas-sucursales" : "compras.requisiciones.gestionar-todas-sucursales",
            permisos, (uid, c) => sucursales.EstaAsociadoAsync(uid, info.SucursalId!.Value, c), cancellationToken);
    }
}
