import { ETIQUETA_ESTADO_PEDIDO, ETIQUETA_ESTADO_TIMBRADO } from './glosario';

export type TonoEstado = 'success' | 'warning' | 'danger' | 'info' | 'neutral';
export interface EstadoUi {
  tono: TonoEstado;
  texto: string;
}
const timbrado: Record<string, EstadoUi> = Object.fromEntries(
  Object.entries(ETIQUETA_ESTADO_TIMBRADO).map(([estado, texto]) => [
    estado,
    {
      texto,
      tono:
        estado === 'Timbrado'
          ? 'success'
          : estado === 'TimbradoFallido'
            ? 'danger'
            : ['Cancelado', 'Descartada', 'CancelacionPendiente', 'Borrador'].includes(estado)
              ? 'neutral'
              : estado === 'PendientePedimento' || estado === 'PorAutorizar'
                ? 'warning'
                : 'info',
    },
  ]),
);
export const ESTADOS_UI: Record<string, Record<string, EstadoUi>> = {
  pedido: Object.fromEntries(
    Object.entries(ETIQUETA_ESTADO_PEDIDO).map(([estado, texto]) => [
      estado,
      {
        texto,
        tono:
          estado === 'Facturado'
            ? 'success'
            : estado === 'Excepcion'
              ? 'danger'
              : estado === 'Cancelado'
                ? 'neutral'
                : estado === 'Bloqueado'
                  ? 'warning'
                  : 'info',
      },
    ]),
  ),
  factura: timbrado,
  rep: timbrado,
  repPendiente: {
    Pendiente: { tono: 'warning', texto: 'Pendiente' },
    Emitido: { tono: 'success', texto: 'Emitido' },
    Descartado: { tono: 'neutral', texto: 'Descartado' },
  },
  cartaPorte: timbrado,
  anticipo: {
    Abierto: { tono: 'success', texto: 'Abierto' },
    Amortizado: { tono: 'info', texto: 'Amortizado' },
    Cancelado: { tono: 'neutral', texto: 'Cancelado' },
  },
  caja: {
    Activo: { tono: 'success', texto: 'Activa' },
    Inactivo: { tono: 'neutral', texto: 'Inactiva' },
  },
  sesion: {
    Abierta: { tono: 'success', texto: 'Abierta' },
    Cerrada: { tono: 'neutral', texto: 'Cerrada' },
    EnArqueo: { tono: 'warning', texto: 'En arqueo' },
  },
  activo: {
    Autorizada: { tono: 'success', texto: 'Autorizada' },
    Usada: { tono: 'info', texto: 'Usada' },
    Cancelada: { tono: 'neutral', texto: 'Cancelada' },
  },
  excepcion: {
    Pendiente: { tono: 'warning', texto: 'Pendiente' },
    Resuelta: { tono: 'success', texto: 'Resuelta' },
  },
  // F6.7 — Cierre del día por sucursal. El backend ya manda el texto final.
  monitorCfdi: {
    Timbrado: { tono: 'success', texto: 'Timbrado' },
    'Cancelación en trámite': { tono: 'neutral', texto: 'Cancelación en trámite' },
    Cancelado: { tono: 'neutral', texto: 'Cancelado' },
    Fallido: { tono: 'danger', texto: 'Fallido' },
    'Sin timbrar': { tono: 'warning', texto: 'Sin timbrar' },
  },
  estatusSat: {
    Vigente: { tono: 'success', texto: 'Vigente' },
    Cancelado: { tono: 'neutral', texto: 'Cancelado' },
    'No encontrado': { tono: 'danger', texto: 'No encontrado' },
    'Sin verificar': { tono: 'info', texto: 'Sin verificar' },
    'No aplica': { tono: 'neutral', texto: 'No aplica' },
  },
  conciliacionCajero: {
    Cuadra: { tono: 'success', texto: 'Cuadra' },
    'Con diferencia': { tono: 'danger', texto: 'Con diferencia' },
  },
  conciliacionPartida: {
    Cuadra: { tono: 'success', texto: 'Cuadra' },
    'Facturada sin cobro': { tono: 'warning', texto: 'Facturada sin cobro' },
    'Cobro parcial': { tono: 'warning', texto: 'Cobro parcial' },
    'Cobrada de más': { tono: 'warning', texto: 'Cobrada de más' },
    'Crédito (PPD) · informativo': { tono: 'neutral', texto: 'Crédito (PPD) · informativo' },
    'Otra moneda · informativo': { tono: 'neutral', texto: 'Otra moneda · informativo' },
    'Cobro de crédito (complemento de pago)': {
      tono: 'info',
      texto: 'Cobro de crédito (complemento de pago)',
    },
    'Cobro de documento cancelado': { tono: 'danger', texto: 'Cobro de documento cancelado' },
    'Cobro de documento de otro día': { tono: 'info', texto: 'Cobro de documento de otro día' },
  },
};
export function estadoUi(entidad: string, estado: string | null): EstadoUi {
  return ESTADOS_UI[entidad]?.[estado ?? ''] ?? { tono: 'neutral', texto: estado ?? '—' };
}
