using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Adjuntos;

namespace Millet.Compras.Application.Adjuntos;

/// <summary>
/// F1-COM-11: evidencia en archivo de cada respuesta del proveedor sobre una OC (G1.2, ADR-0058).
/// El alcance por sucursal se hereda de la OC dueña de la respuesta (ADR-0051).
/// </summary>
public sealed class RespuestaProveedorOcAdjuntoPropietario(Millet.Compras.Infrastructure.ComprasDbContext db,
    ICurrentUserContext user, ICurrentUserPermissions permisos, IUsuarioSucursalReadPort sucursales) : IAdjuntoPropietario
{
    public string TipoEntidad => "oc_respuesta_proveedor";
    public string CodigoNoEncontrado => "RESPUESTA_PROVEEDOR_NO_ENCONTRADA";
    public string PermisoVer => "compras.ordenes.leer";
    public string PermisoSubir => "compras.ordenes.registrar-respuesta-proveedor";
    public string PermisoBaja => "compras.ordenes.registrar-respuesta-proveedor";

    public async Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken)
    {
        // El join con OrdenesCompra aplica el filtro de empresa de la OC.
        var x = await (from r in db.RespuestasProveedorOc.AsNoTracking()
                       join o in db.OrdenesCompra.AsNoTracking() on r.OrdenCompraId equals o.Id
                       where r.Id == entidadId
                       select new { r.Id, r.Numero, o.EmpresaId, o.SucursalDestinoId, o.Folio })
            .FirstOrDefaultAsync(cancellationToken);
        return x is null
            ? null
            : new AdjuntoPropietarioInfo(x.Id, x.EmpresaId, x.SucursalDestinoId, true, null,
                $"{x.Folio.Valor} · respuesta {x.Numero}");
    }

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken)
        => VerificarAlcanceAsync(info, AdjuntoOperacion.Ver, cancellationToken);

    public Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
        => SucursalScopeGuard.VerificarAsync(user.UserId,
            operacion == AdjuntoOperacion.Ver ? "compras.ordenes.leer-todas-sucursales" : "compras.ordenes.gestionar-todas-sucursales",
            permisos, (uid, c) => sucursales.EstaAsociadoAsync(uid, info.SucursalId!.Value, c), cancellationToken);
}
