import { z } from 'zod';
import { esApiError } from '@/lib/api';
import type { EstadoPlantillaRecurrente } from '../api/recurrentes-types';
import { ULTIMO_DIA_DEL_MES } from '../api/recurrentes-types';
import { mensajePoliza, nombrePeriodo } from './polizas';

type VarianteBadge = 'success' | 'warning' | 'info' | 'neutral' | 'danger';

/** Un estado = un color (DESIGN §2.1): borrador neutral, activa success, suspendida warning, terminada info. */
export const VARIANTE_ESTADO_PLANTILLA: Record<EstadoPlantillaRecurrente, VarianteBadge> = {
  Borrador: 'neutral',
  Activa: 'success',
  Suspendida: 'warning',
  Terminada: 'info',
};

/** Rangos del backend (K10.4). */
export const PERIODOS_MINIMO = 1;
export const PERIODOS_MAXIMO = 120;
export const DIA_MAXIMO = 28;
export const NOMBRE_MAXIMO = 120;

/** Códigos de negocio que la UI trata de forma específica. */
export const CODIGO_ACTIVA_QUIEN_CREA = 'CONTAB_RECURRENTE_ACTIVA_QUIEN_CREA';

/** `0` → «Último día del mes»; `5` → «Día 5». */
export function etiquetaDia(dia: number): string {
  return dia === ULTIMO_DIA_DEL_MES ? 'Último día del mes' : `Día ${dia}`;
}

/** `2026, 3` → «Marzo 2026». */
export function etiquetaMes(anio: number, mes: number): string {
  return `${nombrePeriodo(mes)} ${anio}`;
}

/** Último periodo de la vigencia: inicio + (n − 1) meses. */
export function periodoFinal(anioInicio: number, mesInicio: number, numeroPeriodos: number): { anio: number; mes: number } {
  const indice = anioInicio * 12 + (mesInicio - 1) + Math.max(numeroPeriodos, 1) - 1;
  return { anio: Math.floor(indice / 12), mes: (indice % 12) + 1 };
}

/** «Marzo 2026 – Febrero 2027 (12 periodos)». */
export function etiquetaVigencia(anioInicio: number, mesInicio: number, numeroPeriodos: number): string {
  const fin = periodoFinal(anioInicio, mesInicio, numeroPeriodos);
  const periodos = `${numeroPeriodos} ${numeroPeriodos === 1 ? 'periodo' : 'periodos'}`;
  return numeroPeriodos <= 1
    ? `${etiquetaMes(anioInicio, mesInicio)} (${periodos})`
    : `${etiquetaMes(anioInicio, mesInicio)} – ${etiquetaMes(fin.anio, fin.mes)} (${periodos})`;
}

/** Mensaje del servidor en lenguaje de usuario; el conflicto de versión habla de la plantilla. */
export function mensajeRecurrente(e: unknown): string {
  if (esApiError(e) && e.status === 409 && e.code === 'CONCURRENCY_CONFLICT') {
    return 'Alguien más modificó esta plantilla. Recarga para ver la versión actual.';
  }
  if (esApiError(e) && e.status === 428) return 'Falta la versión de la plantilla. Recarga e intenta de nuevo.';
  return mensajePoliza(e);
}

// ─── Search params de la bandeja ──────────────────────────────────────────────

export const RecurrentesSearchSchema = z.object({
  estado: z.enum(['Borrador', 'Activa', 'Suspendida', 'Terminada']).optional().catch(undefined),
});

export type RecurrentesSearch = z.infer<typeof RecurrentesSearchSchema>;
