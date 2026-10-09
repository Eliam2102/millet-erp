import { EstadoOrdenCompra, type SolicitudCancelacionOc } from '../api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

export function puedeResolverCancelacion(estado: EstadoOrdenCompra, solicitudes: readonly SolicitudCancelacionOc[], usuarioId: string | undefined, permisos: readonly string[]) {
  const pendiente = solicitudes.find((s) => s.fechaResolucion == null);
  return estado === EstadoOrdenCompra.CancelacionSolicitada && !!pendiente && !!usuarioId
    && pendiente.solicitanteId !== usuarioId
    && permisos.includes(PermisosCanonicos.ComprasOrdenesAutorizarNivel2);
}
