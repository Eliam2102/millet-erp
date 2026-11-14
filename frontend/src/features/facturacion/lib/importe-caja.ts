/**
 * P54: los campos de importe de caja (fondo de apertura, efectivo contado,
 * movimientos) solo aceptan números. Quita lo que no sea dígito, deja un solo
 * punto decimal y como máximo dos decimales; la coma se toma como punto.
 */
export function limpiarImporte(texto: string): string {
  const soloValidos = texto.replace(/,/g, '.').replace(/[^\d.]/g, '');
  const [entero, ...resto] = soloValidos.split('.');
  if (resto.length === 0) return entero;
  return `${entero}.${resto.join('').slice(0, 2)}`;
}

/** Monto ≥ 0 con hasta dos decimales, o null si el texto no es un importe. */
export function leerImporte(texto: string): number | null {
  if (!/^\d+(\.\d{0,2})?$/.test(texto.trim())) return null;
  return Number(texto);
}
