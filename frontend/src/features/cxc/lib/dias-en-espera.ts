/**
 * C7.4 · antigüedad de un depósito sin identificar: días naturales de la fecha
 * valor a hoy. Los que pasan de {@link DIAS_ESPERA_ALERTA} se resaltan (no bloquea).
 */
export function diasEnEspera(fechaValor: string, hoy: Date = new Date()): number {
  const [a, m, d] = fechaValor.slice(0, 10).split('-').map(Number);
  const inicio = Date.UTC(a, m - 1, d);
  const fin = Date.UTC(hoy.getFullYear(), hoy.getMonth(), hoy.getDate());
  return Math.max(0, Math.round((fin - inicio) / 86_400_000));
}

/**
 * Umbral de aviso: depósitos con más de 5 días sin identificar. Supuesto de la
 * ficha F1-CXC-03 (26-oct, §7.4: «es un parámetro y no bloquea»); si Millet
 * define otro plazo, solo cambia esta constante.
 */
export const DIAS_ESPERA_ALERTA = 5;
