/**
 * Helpers de ordenamiento para tablas client-side. Vive aparte del
 * componente <c>&lt;SortableHeader/&gt;</c> para que React Fast Refresh
 * acepte la separación "componente vs no-componente" (rule
 * <c>react-refresh/only-export-components</c>).
 */

export type SortDir = 'asc' | 'desc';

export interface SortState<K extends string> {
  key: K;
  dir: SortDir;
}

/**
 * Comparador genérico — extrae el valor de la key del item y compara
 * por tipo (números numéricos, strings con <c>localeCompare</c>).
 * <c>null</c>/<c>undefined</c> van al final sin importar la dirección.
 *
 * <para>Para fechas en formato ISO (<c>YYYY-MM-DD</c> o
 * <c>YYYY-MM-DDTHH:mm:ssZ</c>) el orden cronológico coincide con el
 * lexicográfico, así que <c>localeCompare</c> alcanza sin parsear a
 * <c>Date</c>.</para>
 */
export function compareItemsBy<T, K extends keyof T>(
  a: T,
  b: T,
  key: K,
  dir: SortDir,
): number {
  const va = a[key] as unknown;
  const vb = b[key] as unknown;
  if (va == null && vb == null) return 0;
  if (va == null) return 1;
  if (vb == null) return -1;

  const cmp = compararValores(va, vb);
  return dir === 'asc' ? cmp : -cmp;
}

function compararValores(va: unknown, vb: unknown): number {
  if (typeof va === 'number' && typeof vb === 'number') {
    return va - vb;
  }
  if (typeof va === 'string' && typeof vb === 'string') {
    return va.localeCompare(vb, 'es');
  }
  if (typeof va === 'boolean' && typeof vb === 'boolean') {
    return (va ? 1 : 0) - (vb ? 1 : 0);
  }
  return String(va).localeCompare(String(vb), 'es');
}
