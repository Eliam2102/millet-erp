import {
  CanalCobranza,
  EstadoAutorizacionCredito,
  EstadoLineaCredito,
  EstadoPropuestaAplicacion,
  OrigenLineaCredito,
  ReglaAplicadaLiberacion,
  ResultadoCobranza,
  ResultadoLiberacion,
  TipoAlertaCartera,
} from '@/features/cxc/api/types';

/**
 * Etiquetas de UI para los enums del módulo CxC (CXC-FE-PR2+). Mismo rol
 * que <c>facturacion/lib/glosario.ts</c>: los valores numéricos del
 * backend nunca se muestran crudos.
 */

export const ETIQUETA_ESTADO_LINEA: Record<EstadoLineaCredito, string> = {
  [EstadoLineaCredito.Activa]: 'Activa',
  [EstadoLineaCredito.Bloqueada]: 'Bloqueada',
  [EstadoLineaCredito.Suspendida]: 'Suspendida',
};

export const ETIQUETA_ORIGEN_LINEA: Record<OrigenLineaCredito, string> = {
  [OrigenLineaCredito.Solunion]: 'SOLUNION',
  [OrigenLineaCredito.Interno]: 'Interno',
};

export const ETIQUETA_RESULTADO_LIBERACION: Record<ResultadoLiberacion, string> = {
  [ResultadoLiberacion.Liberado]: 'Liberado',
  [ResultadoLiberacion.Retenido]: 'Retenido',
  [ResultadoLiberacion.LiberadoConOverride]: 'Liberado con override',
};

export const ETIQUETA_REGLA_LIBERACION: Record<ReglaAplicadaLiberacion, string> = {
  [ReglaAplicadaLiberacion.Serie]: 'Regla por serie',
  [ReglaAplicadaLiberacion.Credito]: 'Crédito disponible',
  [ReglaAplicadaLiberacion.Override]: 'Override autorizado',
};

export const ETIQUETA_ESTADO_AUTORIZACION: Record<EstadoAutorizacionCredito, string> = {
  [EstadoAutorizacionCredito.Autorizada]: 'Vigente',
  [EstadoAutorizacionCredito.Usada]: 'Usada',
  [EstadoAutorizacionCredito.Cancelada]: 'Cancelada',
};

export const ETIQUETA_CANAL_COBRANZA: Record<CanalCobranza, string> = {
  [CanalCobranza.Llamada]: 'Llamada',
  [CanalCobranza.Correo]: 'Correo',
  [CanalCobranza.Whatsapp]: 'WhatsApp',
};

export const ETIQUETA_RESULTADO_COBRANZA: Record<ResultadoCobranza, string> = {
  [ResultadoCobranza.PromesaPago]: 'Promesa de pago',
  [ResultadoCobranza.SinRespuesta]: 'Sin respuesta',
  [ResultadoCobranza.Excusa]: 'Excusa',
  [ResultadoCobranza.Otro]: 'Otro',
};

export const ETIQUETA_ESTADO_PROPUESTA: Record<EstadoPropuestaAplicacion, string> = {
  [EstadoPropuestaAplicacion.Propuesta]: 'Propuesta',
  [EstadoPropuestaAplicacion.Confirmada]: 'Confirmada',
  [EstadoPropuestaAplicacion.Rechazada]: 'Rechazada',
};

export const ETIQUETA_TIPO_ALERTA: Record<TipoAlertaCartera, string> = {
  [TipoAlertaCartera.Solunion90d]: 'SOLUNION 90 días',
  [TipoAlertaCartera.ExcesoCredito]: 'Exceso de crédito',
  [TipoAlertaCartera.AutoBloqueoVencimiento]: 'Auto-bloqueo por vencimiento',
};

/** Formato de moneda sin conversión: cada monto con su divisa (§5). */
export function formatoMonto(monto: number, moneda: string): string {
  return `${monto.toLocaleString('es-MX', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} ${moneda}`;
}
