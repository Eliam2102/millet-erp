using Millet.SharedKernel.Application.Incidencias;

namespace Millet.CuentasPorCobrar.Domain.Ports.Conciliacion;

/// <summary>
/// M-236: abre la incidencia común (Compartido, U-4) de una diferencia de la conciliación. La corrida programada
/// corre en bypass, sin empresa ni usuario: la implementación abre la incidencia en el contexto de
/// <paramref name="empresaId"/>. Idempotente: una pendiente igual no se duplica.
/// </summary>
public interface IAperturaIncidenciasConciliacionPort
{
    Task<IncidenciaAbierta> AbrirAsync(Guid empresaId, AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken);
}

/// <summary>
/// M-236: responsable por omisión de las incidencias de la conciliación programada (no hay usuario que la ejecute):
/// un usuario activo de la empresa con el permiso de ejecutar la conciliación. Se reasigna desde «Mis incidencias».
/// </summary>
public interface IResponsableConciliacionReadPort
{
    Task<Guid?> ObtenerAsync(Guid empresaId, CancellationToken cancellationToken);
}
