import { z } from 'zod';
import { zId } from '@/lib/z-id';
import type { RelacionRepp } from '../api/useReppPendientes';

export const RevisionReppSchema = z.object({
  formaPago: z
    .string()
    .min(1, 'Selecciona la forma de pago.')
    .refine((v) => v !== '99', 'Indica la forma de pago real.'),
  facturas: z.array(z.object({ facturaVentaId: zId(), importe: z.number().positive() })).min(1),
});
// Misma precisión de las relaciones persistidas; evita comparar sumas binarias de JS.
export function totalRelacion(facturas: RelacionRepp[]): number {
  return facturas.reduce((s, f) => s + Math.round(f.importe * 1_000_000), 0) / 1_000_000;
}
export function bloqueoRelacion(
  monto: number,
  formaPago: string,
  facturas: RelacionRepp[],
): string | null {
  if (!RevisionReppSchema.safeParse({ formaPago, facturas }).success)
    return 'Selecciona facturas con importes positivos y una forma de pago real.';
  if (new Set(facturas.map((f) => f.facturaVentaId)).size !== facturas.length)
    return 'No repitas facturas en la relación.';
  if (Math.round(totalRelacion(facturas) * 1_000_000) !== Math.round(monto * 1_000_000))
    return 'La relación debe sumar exactamente el monto confirmado.';
  return null;
}
export function formatoImporte(monto: number): string {
  return new Intl.NumberFormat('es-MX', { style: 'currency', currency: 'MXN' }).format(monto);
}
