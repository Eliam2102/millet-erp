using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compras.Application.Oc;

internal static class FirmasServicioCeGuard
{
    public static async Task ExigirAsync(OrdenCompra oc, NivelAutorizacion nivel, ICurrentUserPermissions? permisos, CancellationToken ct)
    {
        if (!oc.EsServicioComercioExterior) return;
        var permiso = nivel == NivelAutorizacion.Nivel1 ? PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel1 : PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel2;
        if (permisos is null || !await permisos.TieneAsync(permiso, ct))
            throw new ForbiddenException("CE_SERVICIO_FIRMA_DENEGADA", "No tiene facultad para resolver esta firma del servicio de comercio exterior.");
    }
}
