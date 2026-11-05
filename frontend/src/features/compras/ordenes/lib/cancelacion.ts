import { EstadoOrdenCompra, type SolicitudCancelacionOc } from '../api/types';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** Solicitud que espera la firma de Dirección: sin resolver y sin estar esperando a Almacenes. */
const esperaDireccion = (s: SolicitudCancelacionOc) => s.fechaResolucion == null && !s.esperaAlmacenes;

export function puedeResolverCancelacion(estado: EstadoOrdenCompra, solicitudes: readonly SolicitudCancelacionOc[], usuarioId: string | undefined, permisos: readonly string[]) {
  const pendiente = solicitudes.find(esperaDireccion);
  return estado === EstadoOrdenCompra.CancelacionSolicitada && !!pendiente && !!usuarioId
    && pendiente.solicitanteId !== usuarioId
    && permisos.includes(PermisosCanonicos.ComprasOrdenesAutorizarNivel2);
}

export type EstadoSolicitudUi = {
  clave: 'esperaAlmacenes' | 'aceptadaAlmacenes' | 'rechazadaAlmacenes' | 'resueltaDireccion' | 'pendienteDireccion';
  texto: string;
  variante: 'warning' | 'neutral' | 'info';
};

/**
 * Estado que se muestra de una solicitud de cancelación. Una solicitud que resolvió Almacenes (86e3jvu83) queda con fecha de
 * resolución pero sin resolutor: no es «Pendiente de Dirección».
 */
export function estadoSolicitud(s: SolicitudCancelacionOc): EstadoSolicitudUi {
  if (s.esperaAlmacenes) return { clave: 'esperaAlmacenes', texto: 'Esperando respuesta de Almacenes', variante: 'warning' };
  if (s.respuestaAlmacenes) return { clave: 'rechazadaAlmacenes', texto: 'Rechazada por Almacenes', variante: 'info' };
  if (s.resolutorId && s.fechaResolucion) {
    return { clave: 'resueltaDireccion', texto: s.confirmada ? 'Cancelación confirmada' : 'Cancelación rechazada', variante: s.confirmada ? 'neutral' : 'info' };
  }
  if (s.fechaResolucion) return { clave: 'aceptadaAlmacenes', texto: 'Aceptada por Almacenes', variante: 'neutral' };
  return { clave: 'pendienteDireccion', texto: 'Pendiente de Dirección', variante: 'warning' };
}

/** Aviso de la OC en cancelación solicitada, con la redacción que corresponde a quién debe responder. */
export function avisoCancelacion(estado: EstadoOrdenCompra, solicitudes: readonly SolicitudCancelacionOc[]): string | null {
  if (estado !== EstadoOrdenCompra.CancelacionSolicitada) return null;
  if (solicitudes.some((s) => s.esperaAlmacenes)) {
    return 'Cancelación solicitada a Almacenes. La OC sigue vigente y no se libera nada hasta que Almacenes responda; si ya tiene entrada, la cancelación no procede.';
  }
  return 'Cancelación solicitada. No se puede recibir ni facturar hasta que Dirección resuelva con una persona distinta a quien solicitó.';
}

/** Respuestas de Almacenes a mostrar (cancelaciones que rechazó). */
export function respuestasAlmacenes(solicitudes: readonly SolicitudCancelacionOc[]): string[] {
  return solicitudes.flatMap((s) => (s.respuestaAlmacenes ? [s.respuestaAlmacenes] : []));
}

/** Toast al pedir la cancelación: no se afirma «cancelada» porque, si la OC ya está en Almacenes, queda en espera de su respuesta. */
export const TOAST_CANCELACION_REGISTRADA = (folio: string) =>
  `Cancelación de la OC ${folio} registrada. Si ya estaba en Almacenes, queda en espera de su respuesta.`;
