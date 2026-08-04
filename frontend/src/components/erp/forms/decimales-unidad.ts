/**
 * Utilidades de validación de decimales por unidad (ADR-0046 Etapa 2, PR-2c).
 *
 * El backend (Compras + Almacén) ya valida y rechaza autoritativamente; esto es
 * **advisory** en el frontend (feedback temprano). Regla: una cantidad es válida
 * si tiene ≤ los decimales que permite su unidad. Cuando la unidad NO se resuelve
 * (FK null / string que no matchea ningún código) → cae al fallback global.
 */

/** Decimales permitidos por defecto cuando no se resuelve la unidad (cap legacy de la RQ). */
export const DECIMALES_FALLBACK = 5;

/**
 * Normaliza un código/string de unidad para matchear contra el catálogo:
 * mayúsculas, sin espacios, sin punto final — mismo criterio que la
 * reconciliación del backend (<c>upper(btrim(regexp_replace(s, '\.$', '')))</c>).
 */
export function normalizarCodigoUnidad(s: string | null | undefined): string {
  return (s ?? '').trim().toUpperCase().replace(/\.$/, '');
}

/**
 * <c>true</c> si <paramref name="value"/> cabe en <paramref name="decimales"/>
 * posiciones decimales. Cuenta los dígitos tras el punto en la representación
 * string (mismo criterio que el cap global previo de la RQ).
 */
export function cabeEnDecimales(value: number, decimales: number): boolean {
  if (!Number.isFinite(value)) return false;
  const str = String(value);
  const dot = str.indexOf('.');
  if (dot === -1) return true;
  return str.length - dot - 1 <= decimales;
}

/** Step del input numérico para <paramref name="decimales"/> posiciones (pieza→1, kg→0.001). */
export function stepParaDecimales(decimales: number): number {
  return decimales <= 0 ? 1 : 10 ** -decimales;
}
