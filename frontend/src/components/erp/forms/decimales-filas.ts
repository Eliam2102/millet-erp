import { cabeEnDecimales } from '@/components/erp/forms/decimales-unidad';
import type { DecimalesUnidadLookup } from '@/components/erp/forms/useDecimalesUnidad';

/**
 * Resolución de decimales de una fila de captura de Almacén (ADR-0046 Etapa 2
 * PR-2c, advisory). Una fila puede traer el <c>unidadMedidaId</c> del artículo
 * seleccionado (free-selector: vale, devolución a proveedor, MatRev) o solo el
 * string de unidad de una línea heredada (recepción, salida-vs-RQ, devolución
 * interna). Intenta por id (preciso) y cae al matcheo por código; <c>null</c> si
 * no resuelve → el caller no bloquea (fallback).
 */
export interface UnidadDeFila {
  unidadMedidaId?: string | null;
  unidadMedida?: string | null;
}

export function decimalesDeFila(
  lookup: DecimalesUnidadLookup,
  fila: UnidadDeFila,
): number | null {
  return (
    lookup.porId(fila.unidadMedidaId) ?? lookup.porCodigo(fila.unidadMedida) ?? null
  );
}

/** Item a validar: su índice en el array + la cantidad + su unidad. */
export interface FilaCantidad extends UnidadDeFila {
  cantidad: number | null | undefined;
}

/**
 * Estado de decimales de una fila (ADR-0046, advisory client-side). DOS estados
 * diferenciados — no un booleano:
 * <list>
 *   <item><c>'invalidos'</c>: la unidad resolvió y la cantidad NO cabe en sus
 *     decimales (p. ej. 5.8 en PZA). ERROR bloqueante — dato inválido conocido.</item>
 *   <item><c>'no-resoluble'</c>: la unidad NO matcheó ningún código del catálogo
 *     (id null y string sin match). ADVERTENCIA no bloqueante — el cliente no
 *     sabe; el guard server-side decide al guardar. Nunca bloquea (bloquearía
 *     los artículos de empaque sin unidad reconciliada = paro operativo).</item>
 *   <item><c>'ok'</c>: unidad resuelta y cantidad válida, o aún sin cantidad.</item>
 * </list>
 *
 * <para>Reemplaza el skip silencioso previo: cuando la unidad no resuelve ya no
 * se omite — se reporta como <c>'no-resoluble'</c> para que el caller avise, en
 * vez de fingir que está bien.</para>
 */
export type EstadoDecimalesFila = 'ok' | 'invalidos' | 'no-resoluble';

export function evaluarDecimalesFila(
  lookup: DecimalesUnidadLookup,
  fila: FilaCantidad,
): EstadoDecimalesFila {
  const dec = decimalesDeFila(lookup, fila);
  if (dec == null) return 'no-resoluble';
  const cantidad = fila.cantidad;
  if (cantidad == null || !Number.isFinite(cantidad)) return 'ok';
  return cabeEnDecimales(cantidad, dec) ? 'ok' : 'invalidos';
}

/** Estado por fila (mismo orden que el array de entrada). */
export function evaluarDecimalesFilas(
  lookup: DecimalesUnidadLookup,
  filas: readonly FilaCantidad[],
): EstadoDecimalesFila[] {
  return filas.map((fila) => evaluarDecimalesFila(lookup, fila));
}

/**
 * Índices de las filas con decimales INVÁLIDOS (unidad resuelta + valor que no
 * cabe). NO incluye las <c>'no-resoluble'</c> — esas avisan, no bloquean. El
 * caller marca el error por fila y aborta el submit. El backend valida
 * autoritativo de todos modos.
 */
export function filasConDecimalesInvalidos(
  lookup: DecimalesUnidadLookup,
  filas: readonly FilaCantidad[],
): number[] {
  const malas: number[] = [];
  filas.forEach((fila, i) => {
    if (evaluarDecimalesFila(lookup, fila) === 'invalidos') malas.push(i);
  });
  return malas;
}

/** Error bloqueante: la cantidad excede los decimales de la unidad resuelta. */
export const MENSAJE_DECIMALES_UNIDAD =
  'Demasiados decimales para la unidad seleccionada';

/** Advertencia no bloqueante: la unidad no matcheó el catálogo; el servidor la
 *  valida al guardar. */
export const MENSAJE_UNIDAD_NO_RESOLUBLE =
  'Unidad no reconocida: no se validaron los decimales; el servidor la validará al guardar.';
