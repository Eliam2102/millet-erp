import {
  ESTADO_SOLICITUD_CANCELACION_LABEL,
  type EstadoSolicitudCancelacion,
} from '@/features/facturacion/api/types';

/** H-U31: etiqueta del estado de la cancelación; «Solicitada» con 205 del SAT se muestra como pendiente de reintento. */
export function etiquetaEstadoCancelacion(c: {
  estadoSolicitud: string | null;
  pendienteReintentoSat?: boolean;
}): string {
  if (c.pendienteReintentoSat) return 'Pendiente de reintento';
  if (!c.estadoSolicitud) return '';
  return ESTADO_SOLICITUD_CANCELACION_LABEL[c.estadoSolicitud as EstadoSolicitudCancelacion] ?? c.estadoSolicitud;
}
