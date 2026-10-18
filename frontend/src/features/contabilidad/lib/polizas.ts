import { z } from 'zod';
import { esApiError } from '@/lib/api';
import type { EstadoPoliza, TipoPolizaSat } from '../api/polizas-types';

type VarianteBadge = 'success' | 'warning' | 'info' | 'neutral' | 'danger';

/** Un estado = un color en todo el ERP (DESIGN §2.1): borrador neutral, autorizada success, anulada danger. */
export const VARIANTE_ESTADO_POLIZA: Record<EstadoPoliza, VarianteBadge> = {
  Borrador: 'neutral',
  Autorizada: 'success',
  Anulada: 'danger',
};

/** K10.9: la clave de un tipo de operación real son dos letras (TT, PC, RF…); los de prueba conservan clave libre. */
export const CLAVE_OPERACION = /^[A-Z]{2}$/;

export const ETIQUETA_TIPO_SAT: Record<TipoPolizaSat, string> = { Ingreso: 'Ingreso', Egreso: 'Egreso', Diario: 'Diario' };

/** Motivo de anulación (mismo rango que el backend). */
export const MOTIVO_ANULACION_MINIMO = 10;
export const MOTIVO_ANULACION_MAXIMO = 500;

/** Códigos de negocio de pólizas que la UI trata de forma específica. */
export const CODIGO_REQUIERE_PERIODO = 'CONTAB_POLIZA_ANULACION_REQUIERE_PERIODO';
export const CODIGO_PERIODO_ORIGINAL_ABIERTO = 'CONTAB_POLIZA_ANULACION_PERIODO_ORIGINAL_ABIERTO';

const formato = new Intl.NumberFormat('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/** `1234.5` → `$1,234.50`; negativo `-$1,234.50` (las anulaciones llevan importes negativos y deben verse como tales). */
export function monto(n: number): string {
  const abs = formato.format(Math.abs(n));
  return n < 0 ? `-$${abs}` : `$${abs}`;
}

/** Importe en la columna de cargo/abono: vacío cuando es cero (la partida va de un solo lado). */
export function importeLado(n: number): string {
  return n === 0 ? '' : monto(n);
}

/** Redondeo a centavos para comparar totales sin ruido de coma flotante. */
export function centavos(n: number): number {
  return Math.round(n * 100) / 100;
}

const MESES_LARGOS = ['Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio', 'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre'];

export function nombrePeriodo(numero: number): string {
  return numero === 13 ? 'Ajustes' : (MESES_LARGOS[numero - 1] ?? String(numero));
}

/** `2026 · 03 Marzo` / `2026 · 13 Ajustes`. */
export function etiquetaPeriodo(anio: number, numero: number): string {
  return `${anio} · ${String(numero).padStart(2, '0')} ${nombrePeriodo(numero)}`;
}

/** Folio de la póliza o «Borrador» mientras no se autoriza (K10.9: el folio se asigna al autorizar). */
export function folioOBorrador(folio: string | null): string {
  return folio ?? 'Borrador';
}

/** Mensaje del servidor en lenguaje de usuario (el detalle del Problem Details ya lo es). */
export function mensajePoliza(e: unknown): string {
  if (!esApiError(e)) return 'No se pudo completar la operación (sin conexión o error inesperado). Tus datos se conservan: reintenta.';
  if (e.status === 403) return 'No tienes permiso para esta acción o para operar esa sucursal.';
  if (e.status === 409 && e.code === 'CONCURRENCY_CONFLICT') return 'Alguien más modificó esta póliza. Recarga para ver la versión actual.';
  if (e.status === 428) return 'Falta la versión de la póliza. Recarga e intenta de nuevo.';
  if (e.status >= 500) return `El servidor no pudo procesar la solicitud${e.traceId ? ` (código ${e.traceId})` : ''}. Reintenta.`;
  return e.problem.detail ?? e.problem.title;
}

/**
 * Errores por campo del ValidationProblem (`errores[]` y, de respaldo, `errors{}` de ASP.NET), normalizados a minúsculas
 * en el primer carácter de cada segmento: `Partidas[2].CuentaId` → `partidas[2].cuentaId`.
 */
export function erroresPorCampo(e: unknown): Record<string, string[]> {
  const r: Record<string, string[]> = {};
  if (!esApiError(e)) return r;
  const add = (campo: string, msg: string) => {
    const k = normalizarCampo(campo);
    (r[k] ??= []).push(msg);
  };
  // El ValidationException del backend serializa `{ field, code, message }`; otras respuestas usan `{ campo, mensaje }`.
  for (const x of (e.problem.errores ?? []) as Array<{ campo?: string; mensaje?: string; field?: string; message?: string }>) {
    const campo = x.campo ?? x.field;
    const mensaje = x.mensaje ?? x.message;
    if (campo && mensaje) add(campo, mensaje);
  }
  for (const [k, msgs] of Object.entries(e.problem.errors ?? {})) for (const m of msgs) add(k, m);
  return r;
}

export function normalizarCampo(campo: string): string {
  return campo
    .split('.')
    .map((seg) => (seg ? seg[0].toLowerCase() + seg.slice(1) : seg))
    .join('.');
}

// ─── Search params de la bandeja (URL compartible, P1 → P3 conserva filtros) ──

export const PolizasSearchSchema = z.object({
  anio: z.coerce.number().int().min(2000).max(2100).optional().catch(undefined),
  numero: z.coerce.number().int().min(1).max(13).optional().catch(undefined),
  tipo: z.string().min(1).optional().catch(undefined),
  estado: z.enum(['Borrador', 'Autorizada', 'Anulada']).optional().catch(undefined),
  q: z.string().optional().catch(undefined),
  offset: z.coerce.number().int().min(0).optional().catch(undefined),
});

export type PolizasSearch = z.infer<typeof PolizasSearchSchema>;
