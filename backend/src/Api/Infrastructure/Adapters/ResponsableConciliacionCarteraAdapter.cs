using Millet.Almacen.Domain.Ports;
using Millet.CuentasPorCobrar.Domain.Ports.Conciliacion;
using Millet.Identidad.Domain;

namespace Millet.Api.Infrastructure.Adapters;

/// <summary>
/// M-236: responsable por omisión de las incidencias de la conciliación programada de cartera. Reusa el puerto de
/// destinatarios por permiso (Identidad): el primero (orden estable por id) de los usuarios activos de la empresa con
/// <see cref="PermisosCanonicos.CuentasPorCobrarConciliacionEjecutar"/>. Supuesto a validar con Millet; se reasigna desde
/// «Mis incidencias».
/// </summary>
public sealed class ResponsableConciliacionCarteraAdapter(IDestinatariosAvisoReadPort destinatarios) : IResponsableConciliacionReadPort
{
    public async Task<Guid?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken)
    {
        var usuarios = await destinatarios.UsuariosConPermisoAsync(
            empresaId, PermisosCanonicos.CuentasPorCobrarConciliacionEjecutar, null, cancellationToken);
        return usuarios.Count == 0 ? null : usuarios.Min();
    }
}
