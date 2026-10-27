import type { FacturaAbiertaItem } from '@/features/cxc/api/types';

/** V61: orden de antigüedad (vencimiento y, a igualdad, fecha de timbrado). */
export function ordenarPorAntiguedad<T extends Pick<FacturaAbiertaItem, 'fechaVencimiento' | 'fechaTimbrado'>>(
  facturas: readonly T[],
): T[] {
  return [...facturas].sort(
    (a, b) =>
      a.fechaVencimiento.localeCompare(b.fechaVencimiento) ||
      a.fechaTimbrado.localeCompare(b.fechaTimbrado),
  );
}

/**
 * C7.4 · «Sugerir» (V61, Millet, junta del 8-oct): el ERP solo sugiere. Reparte
 * el monto del depósito empezando por la factura más antigua, sin exceder lo
 * disponible de cada factura (saldo menos propuestas abiertas) ni el monto. No
 * envía nada: los importes quedan editables y una persona revisa y propone.
 */
export function sugerirPorAntiguedad(
  facturas: readonly FacturaAbiertaItem[],
  monto: number,
): Map<string, number> {
  const seleccion = new Map<string, number>();
  let restante = Math.round(monto * 100);
  for (const f of ordenarPorAntiguedad(facturas)) {
    if (restante <= 0) break;
    const tope = Math.round((f.disponible ?? f.saldo) * 100);
    if (tope <= 0) continue;
    const importe = Math.min(tope, restante);
    seleccion.set(f.uuid, importe / 100);
    restante -= importe;
  }
  return seleccion;
}
