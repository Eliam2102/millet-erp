/** Los valores del API ya vienen redondeados por línea a centavos. */
export function montoNetoConteo(lineas: readonly { variacionValorMxn: number | null }[]): number {
  const centavos = lineas.reduce((total, linea) =>
    total + Math.round((linea.variacionValorMxn ?? 0) * 100), 0);
  return centavos / 100;
}
