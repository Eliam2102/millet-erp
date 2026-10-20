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
              : estado === 'PendientePedimento'
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
};
export function estadoUi(entidad: string, estado: string | null): EstadoUi {
  return ESTADOS_UI[entidad]?.[estado ?? ''] ?? { tono: 'neutral', texto: estado ?? '—' };
}
