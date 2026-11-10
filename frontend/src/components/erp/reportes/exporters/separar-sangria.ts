/** Puntos de sangría por cada espacio inicial del texto de una celda. */
const PUNTOS_POR_ESPACIO = 2.5;

/**
 * El motor del PDF descarta los espacios al inicio del texto (también los duros), así que la sangría de una celda de texto
 * (p. ej. rubros y cuentas de los estados financieros) se pasa a margen izquierdo. Devuelve el texto sin esos espacios y los puntos.
 */
export function separarSangria(texto: string): { texto: string; sangria: number } {
  const inicio = /^[\s\u00a0]+/.exec(texto)?.[0].length ?? 0;
  return { texto: texto.slice(inicio), sangria: inicio * PUNTOS_POR_ESPACIO };
}
