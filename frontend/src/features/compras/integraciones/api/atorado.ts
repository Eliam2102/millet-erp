import type { AlmCompraEnvio } from '@/features/compras/integraciones/api/useAlmCompraEnvios';

/** v8 §3: un mensaje que lleva 5 minutos sin acuse se da por atorado (Almacenes levanta su alarma; aquí se ve igual). */
export const MINUTOS_ATORADO = 5;

export function estaAtorado(e: Pick<AlmCompraEnvio, 'estado' | 'enviadoEn' | 'acuseEn'>, ahora = Date.now()): boolean {
  return e.estado === 'ENVIADO' && !e.acuseEn && !!e.enviadoEn && ahora - new Date(e.enviadoEn).getTime() >= MINUTOS_ATORADO * 60_000;
}
