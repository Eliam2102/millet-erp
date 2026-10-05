import { esApiError } from '@/lib/api';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import type { Dimension, EstadoRegla, Requerimiento, RequerimientoEfectivo } from '../api/dimensiones-types';

type VarianteBadge = 'success' | 'warning' | 'info' | 'neutral' | 'danger';

/** Mismos rótulos que el catálogo de Centros de Costo (helper único de vocabulario Dim, 07 §0). */
export const ETIQUETA_DIMENSION: Record<Dimension, string> = {
  Dim1: etiquetaNivel('dim1', 'configuracion'),
  Dim2: etiquetaNivel('dim2', 'configuracion'),
  Dim3: etiquetaNivel('dim3', 'configuracion'),
};

export const ETIQUETA_REQUERIMIENTO: Record<Requerimiento, string> = {
  Obligatorio: 'Obligatoria',
  Opcional: 'Opcional',
  NoAplica: 'No aplica',
};

export const VARIANTE_REQUERIMIENTO: Record<Requerimiento, VarianteBadge> = {
  Obligatorio: 'info',
  Opcional: 'neutral',
  NoAplica: 'neutral',
};

export const VARIANTE_ESTADO_REGLA: Record<EstadoRegla, VarianteBadge> = {
  Vigente: 'success',
  Futura: 'info',
  Cerrada: 'neutral',
};

const MESES = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];

/** `2026-10-04` → `4 oct 2026` (DESIGN §6, fecha completa). */
export function fechaCorta(iso: string | null | undefined): string {
  if (!iso) return '';
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!m) return iso;
  return `${Number(m[3])} ${MESES[Number(m[2]) - 1]} ${m[1]}`;
}

export function vigencia(desde: string | null, hasta: string | null): string {
  if (!desde) return 'Sin regla';
  return hasta ? `${fechaCorta(desde)} – ${fechaCorta(hasta)}` : `Desde ${fechaCorta(desde)}`;
}

/** Explica de dónde sale el requerimiento: regla propia, heredada de la rama, para todos los tipos o sin regla. */
export function origenRequerimiento(r: RequerimientoEfectivo): string {
  if (!r.reglaId) return 'Sin regla: se aplica la configuración por defecto';
  const partes = [r.heredada ? `Heredada de la cuenta ${r.cuentaOrigenCodigo}` : 'Regla de esta cuenta'];
  if (r.paraTodosLosTipos) partes.push('para todos los tipos de documento');
  partes.push(r.vigenteHasta ? `vigente ${fechaCorta(r.vigenteDesde)} – ${fechaCorta(r.vigenteHasta)}` : `desde ${fechaCorta(r.vigenteDesde)}`);
  if (r.esPrueba) partes.push('(regla de prueba)');
  return partes.join(', ');
}

/** Mensaje de error del servidor en lenguaje de usuario (el detalle del Problem Details ya lo es). */
export function mensajeError(e: unknown): string {
  if (!esApiError(e)) return 'No se pudo guardar (sin conexión o error inesperado). Tus datos se conservan: reintenta.';
  if (e.status === 403) return 'No tienes permiso para esta acción o para operar esa sucursal.';
  if (e.status === 409 && e.code === 'CONCURRENCY_CONFLICT') return 'Alguien más modificó este registro. Cierra y vuelve a abrirlo para ver la versión actual.';
  return e.problem.detail ?? e.problem.title;
}
