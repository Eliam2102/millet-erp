import {
  EstadoLineaCredito,
  EstadoPropuestaAplicacion,
  ResultadoLiberacion,
  EstadoAutorizacionCredito,
  ResultadoCobranza,
  TipoAlertaCartera,
} from '../api/types';
import {
  ETIQUETA_ESTADO_LINEA,
  ETIQUETA_ESTADO_PROPUESTA,
  ETIQUETA_RESULTADO_LIBERACION,
  ETIQUETA_ESTADO_AUTORIZACION,
  ETIQUETA_RESULTADO_COBRANZA,
  ETIQUETA_TIPO_ALERTA,
} from './glosario';
export type TonoEstado = 'success' | 'warning' | 'danger' | 'info' | 'neutral';
export interface EstadoUi {
  tono: TonoEstado;
  texto: string;
}
export const ESTADOS_UI = {
  linea: {
    [EstadoLineaCredito.Activa]: {
      tono: 'success',
      texto: ETIQUETA_ESTADO_LINEA[EstadoLineaCredito.Activa],
    },
    [EstadoLineaCredito.Bloqueada]: {
      tono: 'danger',
      texto: ETIQUETA_ESTADO_LINEA[EstadoLineaCredito.Bloqueada],
    },
    [EstadoLineaCredito.Suspendida]: {
      tono: 'neutral',
      texto: ETIQUETA_ESTADO_LINEA[EstadoLineaCredito.Suspendida],
    },
  },
  propuesta: {
    [EstadoPropuestaAplicacion.Propuesta]: {
      tono: 'info',
      texto: ETIQUETA_ESTADO_PROPUESTA[EstadoPropuestaAplicacion.Propuesta],
    },
    [EstadoPropuestaAplicacion.Confirmada]: {
      tono: 'success',
      texto: ETIQUETA_ESTADO_PROPUESTA[EstadoPropuestaAplicacion.Confirmada],
    },
    [EstadoPropuestaAplicacion.Rechazada]: {
      tono: 'danger',
      texto: ETIQUETA_ESTADO_PROPUESTA[EstadoPropuestaAplicacion.Rechazada],
    },
  },
  liberacion: {
    [ResultadoLiberacion.Liberado]: {
      tono: 'success',
      texto: ETIQUETA_RESULTADO_LIBERACION[ResultadoLiberacion.Liberado],
    },
    [ResultadoLiberacion.Retenido]: {
      tono: 'danger',
      texto: ETIQUETA_RESULTADO_LIBERACION[ResultadoLiberacion.Retenido],
    },
    [ResultadoLiberacion.LiberadoConOverride]: {
      tono: 'warning',
      texto: ETIQUETA_RESULTADO_LIBERACION[ResultadoLiberacion.LiberadoConOverride],
    },
    // DESIGN.md §tonos: «Pendiente / Por autorizar» = warning.
    [ResultadoLiberacion.PendienteAutorizacion]: {
      tono: 'warning',
      texto: ETIQUETA_RESULTADO_LIBERACION[ResultadoLiberacion.PendienteAutorizacion],
    },
  },
  autorizacion: {
    [EstadoAutorizacionCredito.Autorizada]: {
      tono: 'success',
      texto: ETIQUETA_ESTADO_AUTORIZACION[EstadoAutorizacionCredito.Autorizada],
    },
    [EstadoAutorizacionCredito.Usada]: {
      tono: 'neutral',
      texto: ETIQUETA_ESTADO_AUTORIZACION[EstadoAutorizacionCredito.Usada],
    },
    [EstadoAutorizacionCredito.Cancelada]: {
      tono: 'neutral',
      texto: ETIQUETA_ESTADO_AUTORIZACION[EstadoAutorizacionCredito.Cancelada],
    },
  },
  cobranza: {
    [ResultadoCobranza.PromesaPago]: {
      tono: 'success',
      texto: ETIQUETA_RESULTADO_COBRANZA[ResultadoCobranza.PromesaPago],
    },
    [ResultadoCobranza.SinRespuesta]: {
      tono: 'neutral',
      texto: ETIQUETA_RESULTADO_COBRANZA[ResultadoCobranza.SinRespuesta],
    },
    [ResultadoCobranza.Excusa]: {
      tono: 'warning',
      texto: ETIQUETA_RESULTADO_COBRANZA[ResultadoCobranza.Excusa],
    },
    [ResultadoCobranza.Otro]: {
      tono: 'neutral',
      texto: ETIQUETA_RESULTADO_COBRANZA[ResultadoCobranza.Otro],
    },
  },
  alerta: {
    [TipoAlertaCartera.Solunion90d]: {
      tono: 'danger',
      texto: ETIQUETA_TIPO_ALERTA[TipoAlertaCartera.Solunion90d],
    },
    [TipoAlertaCartera.ExcesoCredito]: {
      tono: 'warning',
      texto: ETIQUETA_TIPO_ALERTA[TipoAlertaCartera.ExcesoCredito],
    },
    [TipoAlertaCartera.AutoBloqueoVencimiento]: {
      tono: 'info',
      texto: ETIQUETA_TIPO_ALERTA[TipoAlertaCartera.AutoBloqueoVencimiento],
    },
  },
  anticipo: {
    Abierto: { tono: 'success', texto: 'Abierto' },
    Amortizado: { tono: 'info', texto: 'Amortizado' },
    Cancelado: { tono: 'neutral', texto: 'Cancelado' },
  },
  // F1-CXC-05: estado derivado en la consulta de aplicaciones de anticipo.
  anticipoAplicacion: {
    Pendiente: { tono: 'info', texto: 'Pendiente' },
    Parcial: { tono: 'warning', texto: 'Parcial' },
    Aplicado: { tono: 'success', texto: 'Aplicado' },
    Cancelado: { tono: 'neutral', texto: 'Cancelado' },
  },
  atencion: {
    Pendiente: { tono: 'warning', texto: 'Pendiente' },
    Atendida: { tono: 'success', texto: 'Atendida' },
  },
} satisfies Record<string, Record<string, EstadoUi>>;
export function estadoUi(entidad: keyof typeof ESTADOS_UI, estado: string | number): EstadoUi {
  return (
    (ESTADOS_UI[entidad] as Record<string, EstadoUi>)[estado] ?? {
      tono: 'neutral',
      texto: String(estado),
    }
  );
}
